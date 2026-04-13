using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Load .eqp binary file — bỏ qua JSON hoàn toàn.
/// Sử dụng unsafe pointer cast để đọc struct trực tiếp từ byte[],
/// tương đương zero-copy (không tạo intermediate object).
/// </summary>
public static class BinEquipLoader
{
    private const uint EqpMagic  = 0x43484E52u;
    private const ushort FormatVer = 1;

    // ── Structs phản chiếu chính xác layout Rust repr(C) ──

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PartRecord
    {
        public uint   SpriteId;
        public byte   PartType;
        public byte   Layer;
        private ushort _pad;
        public uint   NameOffset;
        public short  DefBonus;
        public short  HpBonus;
        public uint   AnimBlockOffset;
        public byte   AnimBlockCount;
        private byte  _p0; private byte _p1; private byte _p2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AnimBlockHeader
    {
        public byte AnimId;
        public byte FrameCount;
        private ushort _pad;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrameOffset { public sbyte Dx; public sbyte Dy; }

    // ── Kết quả load ──

    public sealed class LoadedPart
    {
        public uint   SpriteId;
        public byte   PartType, Layer;
        public string Name;
        public short  DefBonus, HpBonus;
        // anim_id → frame offset array
        public Dictionary<byte, FrameOffset[]> Anims = new();
    }

    /// <summary>Đọc .eqp và trả về Dictionary sprite_id → LoadedPart.</summary>
    public static Dictionary<uint, LoadedPart> Load(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        uint   magic = br.ReadUInt32();
        ushort ver   = br.ReadUInt16();
        ushort _flags = br.ReadUInt16();
        uint   count = br.ReadUInt32();

        if (magic != EqpMagic) throw new InvalidDataException("Bad .eqp magic");
        if (ver   != FormatVer) throw new InvalidDataException("Version mismatch");

        // Index table
        int indexBase = 12;
        int dataBase  = indexBase + (int)count * 8;

        var index = new (uint id, int offset)[count];
        ms.Position = indexBase;
        for (int i = 0; i < count; i++)
            index[i] = (br.ReadUInt32(), (int)br.ReadUInt32());

        var result = new Dictionary<uint, LoadedPart>((int)count);

        for (int i = 0; i < count; i++)
        {
            ms.Position = dataBase + index[i].offset;

            // Đọc PartRecord: đúng 28 bytes, cast từ buffer
            var recBytes = br.ReadBytes(Marshal.SizeOf<PartRecord>());
            var rec      = MemoryMarshal.Read<PartRecord>(recBytes);

            // Đọc AnimBlocks
            var anims = new Dictionary<byte, FrameOffset[]>(rec.AnimBlockCount);
            for (int a = 0; a < rec.AnimBlockCount; a++)
            {
                var hdrBytes = br.ReadBytes(Marshal.SizeOf<AnimBlockHeader>());
                var hdr      = MemoryMarshal.Read<AnimBlockHeader>(hdrBytes);

                var frames = new FrameOffset[hdr.FrameCount];
                for (int f = 0; f < hdr.FrameCount; f++)
                    frames[f] = new FrameOffset
                    {
                        Dx = br.ReadSByte(),
                        Dy = br.ReadSByte(),
                    };
                anims[hdr.AnimId] = frames;
            }

            // String: đọc null-terminated UTF-8 từ string pool
            // (string pool nằm sau tất cả data blobs)
            string name = ReadPoolString(raw, rec.NameOffset);

            result[rec.SpriteId] = new LoadedPart
            {
                SpriteId = rec.SpriteId,
                PartType = rec.PartType,
                Layer    = rec.Layer,
                Name     = name,
                DefBonus = rec.DefBonus,
                HpBonus  = rec.HpBonus,
                Anims    = anims,
            };
        }

        return result;
    }

    private static string ReadPoolString(byte[] raw, uint offset)
    {
        int start = (int)offset;
        int end   = start;
        while (end < raw.Length && raw[end] != 0) end++;
        return System.Text.Encoding.UTF8.GetString(raw, start, end - start);
    }
}