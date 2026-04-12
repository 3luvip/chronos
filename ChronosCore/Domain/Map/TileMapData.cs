using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Map;

public sealed class TileMapData
{
    public const int TileSize         = 32;
    private const int MaxMapDimension = 2_000;

    // ── Tile type flags ──────────────────────────────────────────────────────
    public const int TypeEmpty       = 0;
    public const int TypeCenter      = 1;
    public const int TypeTop         = 2;
    public const int TypeLeft        = 4;
    public const int TypeRight       = 8;
    public const int TypeTree        = 16;
    public const int TypeWaterfall   = 32;
    public const int TypeWaterflow   = 64;
    public const int TypeTopFall     = 128;
    public const int TypeOutside     = 256;
    public const int TypeDown1Pixel  = 512;
    public const int TypeBridge      = 1024;
    public const int TypeUnderwater  = 2048;
    public const int TypeSolidGround = 4096;
    public const int TypeBottom      = 8192;
    public const int TypeLethal      = 16384;

    public int TileWidth   { get; private set; }
    public int TileHeight  { get; private set; }
    public int PixelWidth  => TileWidth  * TileSize;
    public int PixelHeight => TileHeight * TileSize;
    public int MapId       { get; private set; }
    public int TileSetId   { get; private set; }

    public int[] TileFrames { get; private set; } = Array.Empty<int>();
    public int[] TileTypes  { get; private set; } = Array.Empty<int>();

    private static readonly HashSet<int> DoubleMapIds =
        [45, 46, 48, 51, 52, 103, 112, 113, 115, 117, 118, 119, 120, 121, 125, 129, 130];
    private static readonly HashSet<int> NoWaterEffectMapIds = [54, 55, 56, 57, 138];
    private static readonly HashSet<int> OfflineMapIds = [21, 22, 23, 39, 40, 41];

    public bool IsDoubleMap()   => DoubleMapIds.Contains(MapId);
    public bool IsOfflineMap()  => OfflineMapIds.Contains(MapId);
    public bool IsInAirMap()    => MapId is 45 or 46 or 48;
    public bool IsTrainingMap() => MapId is 39 or 40 or 41;
    public bool HasWaterEffect()=> !NoWaterEffectMapIds.Contains(MapId);
    public bool IsVoDaiMap()    => MapId is 51 or 103 or 112 or 113 or 129 or 130;

    public int TileTypeAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight) return 1000;
        return TileTypes[tileY * TileWidth + tileX];
    }

    public int TileFrameAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight) return -1;
        int raw = TileFrames[tileY * TileWidth + tileX];
        return raw == 0 ? -1 : raw - 1;
    }

    public bool HasFlagAt(int tileX, int tileY, int flag) =>
        (TileTypeAt(tileX, tileY) & flag) == flag;

    public bool HasFlagAtPixel(int px, int py, int flag) =>
        HasFlagAt(px / TileSize, py / TileSize, flag);

    public void LoadFromBytes(byte[] data, int mapId, int tileSetId)
    {
        if (data is null || data.Length < 4)
            throw new ArgumentException("Map data null hoặc < 4 bytes.");

        int width  = (data[0] << 8) | data[1];
        int height = (data[2] << 8) | data[3];

        if (width  <= 0 || width  > MaxMapDimension ||
            height <= 0 || height > MaxMapDimension)
            throw new ArgumentException(
                $"Invalid map dimensions {width}×{height}. Max={MaxMapDimension}.");

        MapId    = mapId;
        TileSetId = tileSetId;
        TileWidth  = width;
        TileHeight = height;

        int n = width * height;
        TileFrames = new int[n];
        TileTypes  = new int[n];

        int offset = 4;
        for (int i = 0; i < n && offset < data.Length; i++, offset++)
            TileFrames[i] = data[offset];
    }

    public void ApplyTypeRule(int[] frameIds, int typeFlag)
    {
        int n = TileFrames.Length;
        for (int i = 0; i < n; i++)
        {
            int f = TileFrames[i];
            foreach (int id in frameIds)
                if (f == id) { TileTypes[i] |= typeFlag; break; }
        }
    }

    public void BuildTypes(TileTypeRule[] rules)
    {
        if (rules is null) return;
        foreach (var r in rules) ApplyTypeRule(r.FrameIds, r.TypeFlag);
    }

    internal void InitializeForTesting(int mapId, int tileSetId,
        int w, int h, int[] frames, int[] types)
    {
        MapId = mapId; TileSetId = tileSetId;
        TileWidth = w; TileHeight = h;
        TileFrames = frames; TileTypes = types;
    }
}

public readonly record struct TileTypeRule(int[] FrameIds, int TypeFlag);
