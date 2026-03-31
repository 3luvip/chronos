using Godot;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

/// <summary>
/// TCP client cho Chronos protocol v2.
/// Tính năng:
///   • TLS (optional, SkipTlsCertValidation cho dev)
///   • HMAC-SHA256 integrity trên mỗi frame (optional)
///   • LoginAsync / LogoutAsync / HeartbeatAsync
///   • SendHeartbeatLoopAsync: vòng lặp tự động gửi heartbeat mỗi 30 giây
///     để giữ session alive (server timeout = 90 giây)
/// </summary>
public sealed class ChronosTcpClient : IDisposable
{
    // ── State ─────────────────────────────────────────────────────────────
    private TcpClient? _tcp;
    private Stream?    _stream;
    private uint       _requestId = 1;
    private readonly ClientOptions _options;

    // ── Session ───────────────────────────────────────────────────────────
    public ulong SessionId { get; private set; }
    public int   UserId    { get; private set; }

    /// <summary>True nếu đã login thành công và chưa logout.</summary>
    public bool IsLoggedIn => SessionId != 0 && UserId != 0;

    // ── HeartBeat ─────────────────────────────────────────────────────────
    /// Interval (ms) giữa 2 heartbeat. Server timeout = 90s → dùng 30s.
    private const int HeartbeatIntervalMs = 30_000;

    // ─────────────────────────────────────────────────────────────────────

    public ChronosTcpClient(ClientOptions options)
    {
        _options = options;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Connect
    // ═════════════════════════════════════════════════════════════════════

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        if (_tcp is { Connected: true }) return;

        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port, ct);
        Stream baseStream = _tcp.GetStream();

        if (_options.UseTls)
        {
            var ssl = new SslStream(
                baseStream,
                leaveInnerStreamOpen: false,
                (_, _, _, errors) => _options.SkipTlsCertValidation || errors == SslPolicyErrors.None);

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host }, ct);
            _stream = ssl;
        }
        else
        {
            _stream = baseStream;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Login
    // ═════════════════════════════════════════════════════════════════════

    public async Task<LoginResult> LoginAsync(
        int serverId, int clientId,
        string username, string password,
        CancellationToken ct)
    {
        EnsureConnected();

        var writer = new PacketWriter();
        writer.WriteInt32(serverId);
        writer.WriteInt32(clientId);
        writer.WriteUtf(username);
        writer.WriteUtf(password);

        uint reqId = _requestId++;
        var frame  = BuildFrame(Protocol.OpLogin, reqId, 0, writer.ToArray());
        await WriteFrameAsync(frame, ct);

        // Đọc phản hồi — server có thể gửi OP_SERVER_MESSAGE trước OP_LOGIN
        while (true)
        {
            var resp = await ReadFrameAsync(ct);

            if (resp.Opcode == Protocol.OpServerMessage)
            {
                var rd   = new PacketReader(resp.Payload);
                _        = rd.ReadInt32();   // client_id echo
                string m = rd.ReadUtf();
                GD.Print($"[Client] Server message: {m}");
                // Tiếp tục đọc để lấy OP_LOGIN result
                continue;
            }

            if (resp.Opcode != Protocol.OpLogin || resp.RequestId != reqId)
                continue;

            var reader = new PacketReader(resp.Payload);
            int cid    = reader.ReadInt32();
            byte status = reader.ReadByte();

            if (cid != clientId)
                return new LoginResult { Ok = false, Error = "Client ID mismatch" };

            if (status == 1)
                return new LoginResult { Ok = false, Error = reader.ReadUtf() };

            var result = new LoginResult
            {
                Ok               = true,
                UserId           = reader.ReadInt32(),
                IsAdmin          = reader.ReadByte() == 1,
                Active           = reader.ReadByte() == 1,
                Gold             = reader.ReadInt32(),
                LastTimeLoginMs  = reader.ReadInt64(),
                LastTimeLogoutMs = reader.ReadInt64(),
                Rewards          = reader.ReadUtf(),
                Ruby             = reader.ReadInt32(),
                MocNap           = reader.ReadInt32(),
                ServerLogin      = reader.ReadInt32(),
                IsUseMaBaoVe     = reader.ReadInt32(),
                MaBaoVe          = reader.ReadInt32(),
                TotalRecharge    = reader.ReadInt32(),
                Vnd              = reader.ReadInt32(),
                SessionIdEcho    = reader.ReadUInt64(),
            };

            SessionId = resp.SessionId;
            UserId    = result.UserId;
            return result;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Heartbeat
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gửi một OP_HEARTBEAT và đọc response của server.
    /// Trả về server timestamp (ms) — dùng để kiểm tra clock drift nếu cần.
    /// </summary>
    public async Task<long> HeartbeatAsync(CancellationToken ct)
    {
        EnsureConnected();
        if (!IsLoggedIn)
            throw new InvalidOperationException("Cannot send heartbeat: not logged in.");

        uint reqId = _requestId++;
        // Payload trống; session_id ở header frame là đủ để server xác thực
        var frame  = BuildFrame(Protocol.OpHeartbeat, reqId, SessionId, Array.Empty<byte>());
        await WriteFrameAsync(frame, ct);

        // Đọc response
        while (true)
        {
            var resp = await ReadFrameAsync(ct);
            if (resp.Opcode != Protocol.OpHeartbeat || resp.RequestId != reqId) continue;

            if (resp.Payload.Length < 8)
                throw new InvalidDataException("Heartbeat response too short.");

            long serverTs = BinaryPrimitives.ReadInt64BigEndian(resp.Payload.AsSpan(0, 8));
            return serverTs;
        }
    }

    /// <summary>
    /// Vòng lặp tự động gửi heartbeat mỗi <see cref="HeartbeatIntervalMs"/> ms.
    /// Gọi hàm này sau khi login thành công và truyền CancellationToken của session.
    /// Hàm này sẽ tự dừng khi token bị cancel hoặc khi gặp lỗi (disconnect).
    /// </summary>
    public async Task SendHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsLoggedIn)
        {
            try
            {
                await Task.Delay(HeartbeatIntervalMs, ct);
                if (!IsLoggedIn) break;

                long serverTs = await HeartbeatAsync(ct);
                long drift    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - serverTs;
                GD.Print($"[Client] Heartbeat OK. Server ts={serverTs}, drift={drift}ms");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Client] Heartbeat error: {ex.Message}");
                break;   // Ngừng loop — LoginScreen sẽ xử lý disconnect
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Logout
    // ═════════════════════════════════════════════════════════════════════

    public async Task LogoutAsync(CancellationToken ct)
    {
        EnsureConnected();
        if (!IsLoggedIn) return;

        var writer = new PacketWriter();
        writer.WriteInt32(UserId);

        var frame = BuildFrame(Protocol.OpLogout, _requestId++, SessionId, writer.ToArray());
        await WriteFrameAsync(frame, ct);

        // Server không trả ACK cho OP_LOGOUT
        SessionId = 0;
        UserId    = 0;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Frame building
    // ═════════════════════════════════════════════════════════════════════

    private Frame BuildFrame(ushort opcode, uint requestId, ulong sessionId, byte[] payload)
    {
        var frame = new Frame
        {
            Opcode    = opcode,
            Flags     = _options.UseHmac ? Protocol.FlagIntegrity : (byte)0,
            RequestId = requestId,
            SessionId = sessionId,
            Payload   = payload,
        };
        return _options.UseHmac ? WithHmac(frame) : frame;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Wire I/O
    // ═════════════════════════════════════════════════════════════════════

    private async Task WriteFrameAsync(Frame frame, CancellationToken ct)
    {
        EnsureConnected();
        var stream = _stream!;

        byte[] header = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0,  2), Protocol.FrameMagic);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2,  2), Protocol.Version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4,  2), frame.Opcode);
        header[6] = frame.Flags;
        header[7] = 0; // reserved
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8,  4), (uint)frame.Payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), frame.RequestId);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(16, 8), frame.SessionId);

        await stream.WriteAsync(header, ct);
        if (frame.Payload.Length > 0)
            await stream.WriteAsync(frame.Payload, ct);
        await stream.FlushAsync(ct);
    }

    private async Task<Frame> ReadFrameAsync(CancellationToken ct)
    {
        EnsureConnected();
        var stream = _stream!;

        byte[] header = await ReadExactAsync(stream, 24, ct);

        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        if (magic != Protocol.FrameMagic)
            throw new InvalidDataException($"Invalid magic: 0x{magic:X4}");

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (version != Protocol.Version)
            throw new InvalidDataException($"Unsupported version: {version}");

        ushort opcode    = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        byte   flags     = header[6];
        byte   reserved  = header[7];
        if (reserved != 0)
            throw new InvalidDataException("Reserved byte must be zero");

        uint   payloadLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        uint   requestId  = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4));
        ulong  sessionId  = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16, 8));

        byte[] payload = payloadLen > 0
            ? await ReadExactAsync(stream, (int)payloadLen, ct)
            : Array.Empty<byte>();

        var frame = new Frame
        {
            Opcode    = opcode,
            Flags     = flags,
            RequestId = requestId,
            SessionId = sessionId,
            Payload   = payload,
        };

        if (_options.UseHmac && (flags & Protocol.FlagIntegrity) != 0)
            VerifyAndStripHmac(frame);

        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int size, CancellationToken ct)
    {
        byte[] buf    = new byte[size];
        int    offset = 0;
        while (offset < size)
        {
            int read = await stream.ReadAsync(buf.AsMemory(offset, size - offset), ct);
            if (read <= 0)
                throw new IOException("Connection closed while reading frame");
            offset += read;
        }
        return buf;
    }

    // ═════════════════════════════════════════════════════════════════════
    // HMAC helpers
    // ═════════════════════════════════════════════════════════════════════

    private Frame WithHmac(Frame frame)
    {
        if (string.IsNullOrEmpty(_options.HmacSecret))
            throw new InvalidOperationException("HMAC enabled but secret is empty");

        byte[] tag     = ComputeHmac(frame, frame.Payload, _options.HmacSecret);
        byte[] payload = new byte[frame.Payload.Length + tag.Length];
        Buffer.BlockCopy(frame.Payload, 0, payload, 0,                  frame.Payload.Length);
        Buffer.BlockCopy(tag,           0, payload, frame.Payload.Length, tag.Length);

        return new Frame
        {
            Opcode    = frame.Opcode,
            Flags     = (byte)(frame.Flags | Protocol.FlagIntegrity),
            RequestId = frame.RequestId,
            SessionId = frame.SessionId,
            Payload   = payload,
        };
    }

    private void VerifyAndStripHmac(Frame frame)
    {
        if ((frame.Flags & Protocol.FlagIntegrity) == 0) return;
        if (string.IsNullOrEmpty(_options.HmacSecret))
            throw new InvalidOperationException("HMAC enabled but secret is empty");
        if (frame.Payload.Length < 32)
            throw new InvalidDataException("Invalid HMAC payload (too short)");

        int    split   = frame.Payload.Length - 32;
        byte[] body    = new byte[split];
        byte[] recvTag = new byte[32];
        Buffer.BlockCopy(frame.Payload, 0,     body,    0, split);
        Buffer.BlockCopy(frame.Payload, split, recvTag, 0, 32);

        byte[] expected = ComputeHmac(frame, body, _options.HmacSecret);
        if (!CryptographicOperations.FixedTimeEquals(expected, recvTag))
            throw new InvalidDataException("HMAC mismatch — possible tampering");

        frame.Payload = body;
    }

    private static byte[] ComputeHmac(Frame frame, byte[] payload, string secret)
    {
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        Span<byte> op   = stackalloc byte[2];
        Span<byte> req  = stackalloc byte[4];
        Span<byte> sess = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(op,   frame.Opcode);
        BinaryPrimitives.WriteUInt32BigEndian(req,  frame.RequestId);
        BinaryPrimitives.WriteUInt64BigEndian(sess, frame.SessionId);
        hmac.TransformBlock(op.ToArray(),   0, 2, null, 0);
        hmac.TransformBlock(req.ToArray(),  0, 4, null, 0);
        hmac.TransformBlock(sess.ToArray(), 0, 8, null, 0);
        hmac.TransformFinalBlock(payload, 0, payload.Length);
        return hmac.Hash ?? Array.Empty<byte>();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Misc
    // ═════════════════════════════════════════════════════════════════════

    private void EnsureConnected()
    {
        if (_tcp is null || _stream is null || !_tcp.Connected)
            throw new InvalidOperationException("Client is not connected");
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); _tcp?.Close(); }
        catch (Exception e) { GD.PrintErr($"[Client] Dispose error: {e.Message}"); }
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed class ClientOptions
{
    public bool   UseTls                { get; init; }
    public bool   SkipTlsCertValidation { get; init; } = true;
    public bool   UseHmac               { get; init; }
    public string HmacSecret            { get; init; } = "";
}

public sealed class LoginResult
{
    public bool   Ok               { get; init; }
    public string Error            { get; init; } = "";
    public int    UserId           { get; init; }
    public bool   IsAdmin          { get; init; }
    public bool   Active           { get; init; }
    public int    Gold             { get; init; }
    public long   LastTimeLoginMs  { get; init; }
    public long   LastTimeLogoutMs { get; init; }
    public string Rewards          { get; init; } = "";
    public int    Ruby             { get; init; }
    public int    MocNap           { get; init; }
    public int    ServerLogin      { get; init; }
    public int    IsUseMaBaoVe     { get; init; }
    public int    MaBaoVe          { get; init; }
    public int    TotalRecharge    { get; init; }
    public int    Vnd              { get; init; }
    public ulong  SessionIdEcho    { get; init; }
}