using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// ═══════════════════════════════════════════════════════════════════════════
// InputPayload — dữ liệu input client gửi lên server mỗi 20ms
// ═══════════════════════════════════════════════════════════════════════════
public sealed class InputPayload
{
    public Vector2 MoveDir { get; init; }
    public bool    Attack  { get; init; }
    public bool    Jump    { get; init; }

    /// True nếu có bất kỳ input nào khác zero/false
    public bool HasAny =>
        MoveDir.LengthSquared() > 0.0001f || Attack || Jump;

    // Bitmask flags gửi qua mạng (1 byte, tiết kiệm bandwidth)
    public byte ToFlags()
    {
        byte f = 0;
        if (Attack) f |= 0x01;
        if (Jump)   f |= 0x02;
        return f;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PlayerDelta — state delta nhận từ server (phản chiếu BinPacketReader)
// ═══════════════════════════════════════════════════════════════════════════
public sealed class PlayerDelta
{
    public uint    PlayerId    { get; init; }
    public Vector2 Position    { get; init; }
    public byte    AnimState   { get; init; }   // AnimationController.State cast
    public bool    EquipChanged { get; init; }
    public byte    PartType    { get; init; }
    public ushort  SpriteId    { get; init; }
    public byte?   HpPct       { get; init; }

    /// Tạo từ BinPacketReader.PlayerDelta (bridge)
    public static PlayerDelta FromBin(BinPacketReader.PlayerDelta raw)
    {
        var pos = raw.Pos.HasValue
            ? new Vector2(raw.Pos.Value.x, raw.Pos.Value.y)
            : Vector2.Zero;

        byte animState = raw.Anim.HasValue ? raw.Anim.Value.state : (byte)0;

        bool equipChanged = raw.Equip.HasValue;
        byte partType  = raw.Equip.HasValue ? raw.Equip.Value.part     : (byte)0;
        ushort spriteId = raw.Equip.HasValue ? raw.Equip.Value.spriteId : (ushort)0;

        return new PlayerDelta
        {
            PlayerId    = raw.PlayerId,
            Position    = pos,
            AnimState   = animState,
            EquipChanged = equipChanged,
            PartType    = partType,
            SpriteId    = spriteId,
            HpPct       = raw.HpPct,
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PartRegistry — singleton lookup cho CharacterPart theo (partType, spriteId)
// ═══════════════════════════════════════════════════════════════════════════
public static class PartRegistry
{
    // key: (partType, spriteId)
    private static readonly Dictionary<(byte, ushort), CharacterPart> _cache = new();

    /// Đăng ký một part (gọi khi load .eqp xong)
    public static void Register(byte partType, ushort spriteId, CharacterPart part)
        => _cache[(partType, spriteId)] = part;

    /// Lấy part; trả về null nếu chưa load
    public static CharacterPart? Get(byte partType, ushort spriteId)
        => _cache.TryGetValue((partType, spriteId), out var p) ? p : null;

    /// Load từ BinEquipLoader kết quả và đăng ký toàn bộ
    public static void LoadFromBin(Dictionary<uint, BinEquipLoader.LoadedPart> loaded)
    {
        foreach (var lp in loaded.Values)
        {
            // Convert anim offsets: byte animId → string animName
            var offsets = new Dictionary<string, PartFrame[]>(lp.Anims.Count);
            foreach (var (animId, frames) in lp.Anims)
            {
                string name = animId switch
                {
                    0 => "idle",
                    1 => "run",
                    2 => "attack",
                    3 => "jump",
                    4 => "die",
                    _ => $"anim_{animId}",
                };
                var pf = new PartFrame[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                    pf[i] = new PartFrame(frames[i].Dx, frames[i].Dy);
                offsets[name] = pf;
            }

            var part = new CharacterPart
            {
                PartType = lp.PartType switch
                {
                    0 => "legs",
                    1 => "body",
                    2 => "weapon",
                    3 => "head",
                    4 => "aura",
                    _ => $"part_{lp.PartType}",
                },
                SpriteId = (int)lp.SpriteId,
                Layer    = lp.Layer,
                Offsets  = offsets,
            };

            Register(lp.PartType, (ushort)lp.SpriteId, part);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PlayerNetSync — gửi input & nhận delta từ server
// ═══════════════════════════════════════════════════════════════════════════
public partial class PlayerNetSync : Node
{
    // ── Dependencies (inject từ ngoài hoặc qua _Ready) ────────────────────
    private ChronosTcpClient?    _client;
    private AnimationController? _anim;
    private CharacterRenderer?   _renderer;
    private CancellationTokenSource _cts = new();

    // ── Client-side prediction ────────────────────────────────────────────
    private Vector2 _predictedPos = Vector2.Zero;
    private const float ReconcileThreshold = 1.5f;
    private const float ReconcileLerp      = 0.5f;

    // ── Sequence number chống replay ─────────────────────────────────────
    private uint _inputSeq = 0;

    // ── Anti-cheat: lưu lịch sử input để so sánh khi server trả correction
    private readonly Queue<(uint seq, InputPayload input, long sentMs)> _pendingInputs = new();
    private const int MaxPendingInputs = 60;   // ~1.2s ở 50Hz

    // ── Inject dependencies ───────────────────────────────────────────────
    public void Init(ChronosTcpClient client, AnimationController anim, CharacterRenderer renderer)
    {
        _client   = client;
        _anim     = anim;
        _renderer = renderer;
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Input loop — 50Hz
    // ═════════════════════════════════════════════════════════════════════

    /// Gửi input lên server 50Hz. Gọi sau Init() và sau khi login thành công.
    public async Task StartInputLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var input = GatherInput();
            if (input.HasAny)
            {
                await SendInputAsync(input, _cts.Token);
            }
            await Task.Delay(20, _cts.Token);
        }
    }

    /// Đóng gói và gửi OP_PLAYER_INPUT.
    /// Format payload (big-endian):
    ///   u32  seq
    ///   f32  move_x
    ///   f32  move_y
    ///   u8   flags  (bit0=attack, bit1=jump)
    private async Task SendInputAsync(InputPayload input, CancellationToken ct)
    {
        if (_client is null || !_client.IsLoggedIn) return;

        uint seq = ++_inputSeq;

        // Ghi lịch sử để reconcile
        _pendingInputs.Enqueue((seq, input, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        while (_pendingInputs.Count > MaxPendingInputs)
            _pendingInputs.Dequeue();

        var w = new PacketWriter();
        w.WriteInt32((int)seq);
        WriteFloat(w, input.MoveDir.X);
        WriteFloat(w, input.MoveDir.Y);
        w.WriteByte(input.ToFlags());

        // Gửi bằng SendRawAsync (mở rộng ChronosTcpClient bên dưới)
        await _client.SendPlayerInputAsync(w.ToArray(), ct);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Server delta handler
    // ═════════════════════════════════════════════════════════════════════

    /// Gọi từ network receive loop mỗi khi đọc được OP_PLAYER_DELTA batch.
    public void OnServerDeltaBatch(ReadOnlySpan<byte> payload)
    {
        var deltas = BinPacketReader.ReadBatch(payload);
        foreach (var raw in deltas)
            OnServerDelta(PlayerDelta.FromBin(raw));
    }

    public void OnServerDelta(PlayerDelta delta)
    {
        // ── Server position correction ────────────────────────────────────
        float error = (_predictedPos - delta.Position).Length();
        if (error > ReconcileThreshold)
        {
            // Snap nếu lệch quá 5 tile (teleport detection phía client)
            _predictedPos = error > 5f
                ? delta.Position
                : _predictedPos.Lerp(delta.Position, ReconcileLerp);
        }

        // ── Animation sync ────────────────────────────────────────────────
        if (_anim is not null && delta.AnimState < 5)
            _anim.Transition((AnimationController.State)delta.AnimState);

        // ── Equipment sync ────────────────────────────────────────────────
        if (delta.EquipChanged && _renderer is not null)
        {
            var part = PartRegistry.Get(delta.PartType, delta.SpriteId);
            if (part is not null)
                _renderer.SetPart(part);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    private InputPayload GatherInput() => new()
    {
        MoveDir = Input.GetVector("move_left", "move_right", "move_up", "move_down"),
        Attack  = Input.IsActionJustPressed("attack"),
        Jump    = Input.IsActionJustPressed("jump"),
    };

    // Ghi f32 big-endian vào PacketWriter (protocol dùng BE cho scalars)
    private static void WriteFloat(PacketWriter w, float v)
    {
        var bits = BitConverter.SingleToUInt32Bits(v);
        w.WriteByte((byte)(bits >> 24));
        w.WriteByte((byte)(bits >> 16));
        w.WriteByte((byte)(bits >>  8));
        w.WriteByte((byte) bits);
    }
}