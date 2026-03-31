using System;
using System.IO;
using System.Runtime.CompilerServices;

public static class BinPacketReader
{
    [Flags]
    public enum DeltaFlags : byte
    {
        HasPos   = 0x01,
        HasAnim  = 0x02,
        HasEquip = 0x04,
        HasHp    = 0x08,
    }

    public struct PlayerDelta
    {
        public uint   PlayerId;
        public DeltaFlags Flags;
        public (float x, float y)? Pos;
        public (byte state, byte frame)? Anim;
        public (byte part, ushort spriteId)? Equip;
        public byte?  HpPct;
    }

    /// Đọc batch packet — gọi mỗi khi nhận được OP_PLAYER_DELTA
    public static PlayerDelta[] ReadBatch(ReadOnlySpan<byte> payload)
    {
        int pos     = 0;
        ushort count = ReadU16(payload, ref pos);
        var deltas  = new PlayerDelta[count];

        for (int i = 0; i < count; i++)
        {
            ref var d = ref deltas[i];
            d.PlayerId = ReadU32(payload, ref pos);
            d.Flags    = (DeltaFlags)payload[pos++];

            if ((d.Flags & DeltaFlags.HasPos) != 0)
            {
                // Half-float: decode 2×u16 → float
                ushort xb = ReadU16(payload, ref pos);
                ushort yb = ReadU16(payload, ref pos);
                d.Pos = (HalfToFloat(xb), HalfToFloat(yb));
            }
            if ((d.Flags & DeltaFlags.HasAnim) != 0)
            {
                byte packed = payload[pos++];
                d.Anim = ((byte)(packed & 0x07), (byte)((packed >> 3) & 0x1F));
            }
            if ((d.Flags & DeltaFlags.HasEquip) != 0)
            {
                byte part = payload[pos++];
                ushort sid = ReadU16(payload, ref pos);
                d.Equip = (part, sid);
            }
            if ((d.Flags & DeltaFlags.HasHp) != 0)
                d.HpPct = payload[pos++];
        }
        return deltas;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ushort ReadU16(ReadOnlySpan<byte> s, ref int pos)
    {
        ushort v = (ushort)(s[pos] | (s[pos + 1] << 8));
        pos += 2; return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint ReadU32(ReadOnlySpan<byte> s, ref int pos)
    {
        uint v = (uint)(s[pos] | (s[pos+1]<<8) | (s[pos+2]<<16) | (s[pos+3]<<24));
        pos += 4; return v;
    }

    // IEEE 754 half-float (binary16) → float32
    static float HalfToFloat(ushort h)
    {
        uint sign = (uint)(h >> 15) << 31;
        uint exp  = (uint)((h >> 10) & 0x1F);
        uint mant = (uint)(h & 0x3FF);
        uint f;
        if (exp == 0)       f = sign | (mant << 13);
        else if (exp == 31) f = sign | 0x7F800000 | (mant << 13);
        else                f = sign | ((exp + 112) << 23) | (mant << 13);
        return Unsafe.As<uint, float>(ref f);
    }
}