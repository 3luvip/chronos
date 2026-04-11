using Godot;
using System;
using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Immutable (after loading) container for raw map data: tile frame IDs,
    /// type bitmasks, and map metadata. Loaded from a binary format originally
    /// authored for Java ME — the format is preserved for server compatibility.
    ///
    /// This class is pure data — zero Godot render calls.
    ///
    /// Security note: all inputs from the server must pass the size sanity check
    /// in <see cref="LoadFromBytes"/> before any array allocation occurs.
    /// </summary>
    public sealed class TileMapData
    {
        // ── Public constants ─────────────────────────────────────────────────────

        /// <summary>Tile edge length in pixels. All coordinate math uses this constant.</summary>
        public const int TILE_SIZE = 32;

        /// <summary>Upper bound for map dimensions; protects against malformed server data.</summary>
        private const int MAX_MAP_DIMENSION = 2_000;

        // ── Tile type bitmask flags ───────────────────────────────────────────────
        public const int TYPE_EMPTY        = 0;
        public const int TYPE_CENTER       = 1;
        public const int TYPE_TOP          = 2;
        public const int TYPE_LEFT         = 4;
        public const int TYPE_RIGHT        = 8;
        public const int TYPE_TREE         = 16;
        public const int TYPE_WATERFALL    = 32;
        public const int TYPE_WATERFLOW    = 64;
        public const int TYPE_TOP_FALL     = 128;
        public const int TYPE_OUTSIDE      = 256;
        public const int TYPE_DOWN_1_PIXEL = 512;
        public const int TYPE_BRIDGE       = 1024;
        public const int TYPE_UNDERWATER   = 2048;
        public const int TYPE_SOLID_GROUND = 4096;
        public const int TYPE_BOTTOM       = 8192;
        public const int TYPE_LETHAL       = 16384;
        public const int TYPE_SNAKE        = 32768;
        public const int TYPE_BANG         = 65536;
        public const int TYPE_JUMP_8       = 131072;
        public const int TYPE_NO_THROUGH_0 = 262144;
        public const int TYPE_NO_THROUGH_1 = 524288;

        // ── Dimensions ────────────────────────────────────────────────────────────

        public int TileWidth   { get; private set; }
        public int TileHeight  { get; private set; }
        public int PixelWidth  => TileWidth  * TILE_SIZE;
        public int PixelHeight => TileHeight * TILE_SIZE;

        // ── Raw data arrays ───────────────────────────────────────────────────────

        /// <summary>
        /// Flat array of 1-based tile frame indices (0 = empty cell).
        /// Indexed as <c>TileFrames[y * TileWidth + x]</c>.
        /// </summary>
        public int[] TileFrames { get; private set; }

        /// <summary>
        /// Flat array of tile type bitmasks.
        /// Indexed as <c>TileTypes[y * TileWidth + x]</c>.
        /// </summary>
        public int[] TileTypes { get; private set; }

        // ── Metadata ──────────────────────────────────────────────────────────────

        public int MapId   { get; private set; }
        public int TileSetId  { get; private set; }
        public int ZoneId  { get; set; }
        public int BackgroundId  { get; set; }
        public int BackgroundType { get; set; }

        // ── Map-category lookup sets (O(1) membership tests) ──────────────────────

        private static readonly HashSet<int> DoubleMapIds = new HashSet<int>
        {
            45, 46, 48, 51, 52, 103, 112, 113, 115, 117, 118, 119,
            120, 121, 125, 129, 130
        };

        private static readonly HashSet<int> NoWaterEffectMapIds = new HashSet<int>
        {
            54, 55, 56, 57, 138
        };

        private static readonly HashSet<int> OfflineMapIds = new HashSet<int>
        {
            21, 22, 23, 39, 40, 41
        };

        public bool IsDoubleMap()   => DoubleMapIds.Contains(MapId);
        public bool IsOfflineMap()  => OfflineMapIds.Contains(MapId);
        public bool IsInAirMap()    => MapId == 45 || MapId == 46 || MapId == 48;
        public bool IsTrainingMap() => MapId == 39 || MapId == 40 || MapId == 41;

        /// <summary>Returns true if animated water overlay effects should be rendered.</summary>
        public bool HasWaterEffect() => !NoWaterEffectMapIds.Contains(MapId);

        public bool IsVoDaiMap()
            => MapId == 51 || MapId == 103 || MapId == 112 ||
               MapId == 113 || MapId == 129 || MapId == 130;

        // ── Tile access ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the type bitmask at tile coordinates (tileX, tileY).
        /// Returns 1000 for out-of-bounds coordinates so callers can safely
        /// test any flag without a bounds check.
        /// </summary>
        public int TileTypeAt(int tileX, int tileY)
        {
            if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight)
                return 1000;
            return TileTypes[tileY * TileWidth + tileX];
        }

        /// <summary>Returns the type bitmask at pixel-space coordinates.</summary>
        public int TileTypeAtPixel(int pixelX, int pixelY)
            => TileTypeAt(pixelX / TILE_SIZE, pixelY / TILE_SIZE);

        /// <summary>Tests a specific flag at tile coordinates.</summary>
        public bool HasFlagAt(int tileX, int tileY, int flag)
            => (TileTypeAt(tileX, tileY) & flag) == flag;

        /// <summary>Tests a specific flag at pixel-space coordinates.</summary>
        public bool HasFlagAtPixel(int pixelX, int pixelY, int flag)
            => (TileTypeAtPixel(pixelX, pixelY) & flag) == flag;

        /// <summary>
        /// Returns the zero-based frame index at tile (tileX, tileY).
        /// Returns -1 for empty tiles and out-of-bounds coordinates.
        /// </summary>
        public int TileFrameAt(int tileX, int tileY)
        {
            if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight)
                return -1;
            int rawFrame = TileFrames[tileY * TileWidth + tileX];
            return rawFrame == 0 ? -1 : rawFrame - 1; // 1-based → 0-based
        }

        /// <summary>Returns the world-Y of the tile row containing <paramref name="pixelY"/>.</summary>
        public int TileRowPixelY(int pixelY) => (pixelY / TILE_SIZE) * TILE_SIZE;

        /// <summary>Returns the world-X of the tile column containing <paramref name="pixelX"/>.</summary>
        public int TileColumnPixelX(int pixelX) => (pixelX / TILE_SIZE) * TILE_SIZE;

        // ── Loading ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads map data from a raw byte array in the original binary format:
        ///   Bytes 0–1: map width  (big-endian unsigned short)
        ///   Bytes 2–3: map height (big-endian unsigned short)
        ///   Bytes 4… : tile frame IDs (one unsigned byte per tile, row-major)
        ///
        /// Throws <see cref="ArgumentException"/> if data is null, too short,
        /// or reports dimensions exceeding <see cref="MAX_MAP_DIMENSION"/>.
        /// Never trusts the server — validates every field before allocation.
        /// </summary>
        public void LoadFromBytes(byte[] data, int mapId, int tileSetId)
        {
            if (data == null || data.Length < 4)
                throw new ArgumentException(
                    "TileMapData: received fewer than 4 bytes — data is null or truncated.");

            int width  = (data[0] << 8) | data[1];
            int height = (data[2] << 8) | data[3];

            if (width <= 0 || width > MAX_MAP_DIMENSION ||
                height <= 0 || height > MAX_MAP_DIMENSION)
                throw new ArgumentException(
                    $"TileMapData: invalid map dimensions {width}×{height} received from server. " +
                    $"Maximum allowed is {MAX_MAP_DIMENSION}×{MAX_MAP_DIMENSION}.");

            MapId     = mapId;
            TileSetId = tileSetId;
            TileWidth  = width;
            TileHeight = height;

            int cellCount = width * height;
            TileFrames = new int[cellCount];
            TileTypes  = new int[cellCount];

            int offset = 4;
            for (int index = 0; index < cellCount && offset < data.Length; index++, offset++)
                TileFrames[index] = data[offset]; // unsigned byte, preserved as-is
        }

        /// <summary>
        /// Loads map data from a Godot resource path. Returns false and logs an
        /// error if the file does not exist.
        /// </summary>
        public bool LoadFromGodotResource(string resourcePath, int mapId, int tileSetId)
        {
            if (!FileAccess.FileExists(resourcePath))
            {
                GD.PrintErr($"[TileMapData] File not found: {resourcePath}");
                return false;
            }
            using var file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
            LoadFromBytes(file.GetBuffer((long)file.GetLength()), mapId, tileSetId);
            return true;
        }

        // ── Type rule application ─────────────────────────────────────────────────

        /// <summary>
        /// OR-assigns <paramref name="typeFlag"/> into <see cref="TileTypes"/> for every
        /// tile whose raw frame ID appears in <paramref name="frameIds"/>.
        /// Called after <see cref="LoadFromBytes"/> to build the type array from server rules.
        /// </summary>
        public void ApplyTypeRule(int[] frameIds, int typeFlag)
        {
            int cellCount    = TileFrames.Length;
            int ruleCount    = frameIds.Length;
            for (int index = 0; index < cellCount; index++)
            {
                int frame = TileFrames[index];
                for (int ruleIndex = 0; ruleIndex < ruleCount; ruleIndex++)
                {
                    if (frame == frameIds[ruleIndex])
                    {
                        TileTypes[index] |= typeFlag;
                        break;
                    }
                }
            }
        }

        /// <summary>Applies a batch of type rules in a single pass.</summary>
        public void BuildTypes(TileTypeRule[] rules)
        {
            if (rules == null) return;
            foreach (var rule in rules)
                ApplyTypeRule(rule.FrameIds, rule.TypeFlag);
        }

        // ── Internal test/factory API ─────────────────────────────────────────────

        /// <summary>
        /// Bypasses file loading for unit tests and factory methods.
        /// Not intended for production use.
        /// </summary>
        internal void InitializeForTesting(int mapId, int tileSetId,
                                           int tileWidth, int tileHeight,
                                           int[] frames, int[] types)
        {
            MapId     = mapId;
            TileSetId = tileSetId;
            TileWidth  = tileWidth;
            TileHeight = tileHeight;
            TileFrames = frames;
            TileTypes  = types;
        }
    }

    /// <summary>Associates a set of frame IDs with a tile type bitmask flag.</summary>
    public readonly struct TileTypeRule
    {
        public readonly int[] FrameIds;
        public readonly int   TypeFlag;

        public TileTypeRule(int[] frameIds, int typeFlag)
        {
            FrameIds = frameIds;
            TypeFlag = typeFlag;
        }
    }
}