using Godot;
using System;
using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Production implementation of <see cref="IMapAssetManager"/>.
    ///
    /// Tile loading — three modes detected automatically in priority order:
    ///   1. Direct files      res://t/{frameId}.png   (one PNG per tile frame ID)
    ///   2. Spritesheet       res://t/{tileSetId}.png  (all frames in one image)
    ///   3. Subdirectory      res://t/{tileSetId}/t_01.png … t_NN.png
    ///
    /// Tile textures are held in an LRU cache capped at <see cref="TILE_CACHE_CAPACITY"/>
    /// entries. Background item textures use a separate LRU cache.
    ///
    /// All assets are released on <see cref="UnloadAll"/> (called between maps) to
    /// prevent VRAM leaks. GC.Collect is intentionally NOT called — the runtime
    /// selects collection points that avoid mid-frame hitches.
    ///
    /// Texture filter note:
    ///   Do NOT attempt to set texture_filter on Texture2D resources — it has no
    ///   effect on CanvasItem draw calls. Set <c>TextureFilter = Nearest</c> on the
    ///   <see cref="MapRenderer"/> Node2D instead.
    /// </summary>
    public sealed class MapAssetManager : IMapAssetManager
    {
        private const int TILE_CACHE_CAPACITY       = 512;
        private const int BG_ITEM_CACHE_CAPACITY    = 256;
        private const int SUBDIR_FRAME_SCAN_LIMIT   = 100;

        private MapAssetPaths _paths;

        // ── Tile load mode ────────────────────────────────────────────────────────

        private enum TileLoadMode { Unloaded, DirectFiles, Spritesheet, SubdirectoryFrames }
        private TileLoadMode _tileLoadMode = TileLoadMode.Unloaded;
        private int          _loadedTileSetId = -1;

        // Direct mode — LRU cache keyed by raw frame ID
        private readonly Dictionary<int, Texture2D>           _directTileCache = new();
        private readonly LinkedList<int>                       _directTileLruList  = new();
        private readonly Dictionary<int, LinkedListNode<int>> _directTileLruNodes = new();

        // Spritesheet mode
        private TileSheetInfo? _tileSheetInfo;

        // Subdirectory mode
        private Texture2D[] _subdirectoryFrames;

        // ── Water + misc textures ─────────────────────────────────────────────────

        private readonly Texture2D[] _waterTextures = new Texture2D[5];
        private Texture2D _shadowTexture;
        private Texture2D _lightOverlayTexture;

        // ── Background item LRU cache ─────────────────────────────────────────────

        private readonly Dictionary<int, Texture2D>           _bgItemCache    = new();
        private readonly LinkedList<int>                       _bgItemLruList  = new();
        private readonly Dictionary<int, LinkedListNode<int>> _bgItemLruNodes = new();

        // ── Constructor ───────────────────────────────────────────────────────────

        public MapAssetManager(MapAssetPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        /// <summary>Swaps the path configuration (e.g. when switching resource packs).</summary>
        public void SetPaths(MapAssetPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        // ── IMapAssetManager ─────────────────────────────────────────────────────

        public Texture2D GetTileTexture(int tileSetId, int frameIndex)
        {
            if (tileSetId != _loadedTileSetId) return null;

            return _tileLoadMode switch
            {
                TileLoadMode.DirectFiles =>
                    GetDirectCachedTile(frameIndex + 1), // 0-based → 1-based filename
                TileLoadMode.Spritesheet =>
                    _tileSheetInfo?.Sheet,               // caller uses GetTileSheet for regions
                TileLoadMode.SubdirectoryFrames =>
                    _subdirectoryFrames != null && (uint)frameIndex < (uint)_subdirectoryFrames.Length
                        ? _subdirectoryFrames[frameIndex]
                        : null,
                _ => null
            };
        }

        public TileSheetInfo? GetTileSheet(int tileSetId)
        {
            if (tileSetId != _loadedTileSetId) return null;
            return _tileLoadMode == TileLoadMode.Spritesheet ? _tileSheetInfo : null;
        }

        public Texture2D GetBackgroundItemTexture(int imageId)
        {
            if (_bgItemCache.TryGetValue(imageId, out var cached))
            {
                PromoteLruBgItem(imageId);
                return cached;
            }

            string path    = _paths.BackgroundItemPath(imageId);
            var    texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            CacheBackgroundItemTexture(imageId, texture);
            return texture;
        }

        public Texture2D GetWaterTexture(WaterTextureType type)
            => _waterTextures[(int)type];

        public Texture2D GetShadowTexture()      => _shadowTexture;
        public Texture2D GetLightOverlayTexture() => _lightOverlayTexture;

        public void LoadMapAssets(TileMapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            UnloadAll();
            LoadTileAssets(map.TileSetId);
            LoadWaterAndMiscAssets();
            GD.Print($"[MapAssetManager] Loaded assets — tileSetId={map.TileSetId} mode={_tileLoadMode}");
        }

        public void UnloadAll()
        {
            _tileLoadMode        = TileLoadMode.Unloaded;
            _loadedTileSetId     = -1;
            _tileSheetInfo       = null;
            _subdirectoryFrames  = null;

            _directTileCache.Clear();
            _directTileLruList.Clear();
            _directTileLruNodes.Clear();

            Array.Clear(_waterTextures, 0, _waterTextures.Length);
            _shadowTexture      = null;
            _lightOverlayTexture = null;

            _bgItemCache.Clear();
            _bgItemLruList.Clear();
            _bgItemLruNodes.Clear();

            GD.Print("[MapAssetManager] All assets unloaded.");
        }

        // ── Tile loading ──────────────────────────────────────────────────────────

        private void LoadTileAssets(int tileSetId)
        {
            _loadedTileSetId = tileSetId;

            if (TryLoadDirectFilesMode()) return;
            if (TryLoadSpritesheetMode(tileSetId)) return;
            TryLoadSubdirectoryMode(tileSetId);
        }

        private bool TryLoadDirectFilesMode()
        {
            if (!ResourceLoader.Exists(_paths.TileDirectFramePath(1))) return false;

            _tileLoadMode = TileLoadMode.DirectFiles;
            GD.Print("[MapAssetManager] Tile mode: direct files (res://t/{frameId}.png)");
            return true;
        }

        private bool TryLoadSpritesheetMode(int tileSetId)
        {
            string path = _paths.TileSpritesheetPath(tileSetId);
            if (!ResourceLoader.Exists(path)) return false;

            var sheet = GD.Load<Texture2D>(path);
            if (sheet == null) return false;

            int columns = sheet.GetWidth()  / TileMapData.TILE_SIZE;
            int rows    = sheet.GetHeight() / TileMapData.TILE_SIZE;

            if (columns <= 1 && rows <= 1) return false; // single image, not a sheet

            _tileSheetInfo = new TileSheetInfo(sheet, columns, rows, TileMapData.TILE_SIZE);
            _tileLoadMode  = TileLoadMode.Spritesheet;
            GD.Print($"[MapAssetManager] Tile mode: spritesheet {path} ({columns}×{rows})");
            return true;
        }

        private void TryLoadSubdirectoryMode(int tileSetId)
        {
            var frames = new List<Texture2D>();
            for (int index = 1; index <= SUBDIR_FRAME_SCAN_LIMIT; index++)
            {
                string path = _paths.TileSubdirectoryFramePath(tileSetId, index);
                if (!ResourceLoader.Exists(path)) break;

                var texture = GD.Load<Texture2D>(path);
                if (texture == null) break;
                frames.Add(texture);
            }

            if (frames.Count == 0)
            {
                GD.PrintErr($"[MapAssetManager] No tile textures found for tileSetId={tileSetId}.");
                return;
            }

            _subdirectoryFrames = frames.ToArray();
            _tileLoadMode       = TileLoadMode.SubdirectoryFrames;
            GD.Print($"[MapAssetManager] Tile mode: subdirectory ({_subdirectoryFrames.Length} frames) for tileSetId={tileSetId}");
        }

        // ── Direct tile LRU cache ─────────────────────────────────────────────────

        private Texture2D GetDirectCachedTile(int frameId)
        {
            if (_directTileCache.TryGetValue(frameId, out var cached))
            {
                PromoteLruDirectTile(frameId);
                return cached;
            }

            string    path    = _paths.TileDirectFramePath(frameId);
            Texture2D texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

            if (_directTileCache.Count >= TILE_CACHE_CAPACITY)
                EvictLruDirectTile();

            _directTileCache[frameId]    = texture;
            _directTileLruNodes[frameId] = _directTileLruList.AddFirst(frameId);
            return texture;
        }

        private void PromoteLruDirectTile(int frameId)
        {
            _directTileLruList.Remove(_directTileLruNodes[frameId]);
            _directTileLruNodes[frameId] = _directTileLruList.AddFirst(frameId);
        }

        private void EvictLruDirectTile()
        {
            if (_directTileLruList.Last == null) return;
            int evictedId = _directTileLruList.Last.Value;
            _directTileLruList.RemoveLast();
            _directTileLruNodes.Remove(evictedId);
            _directTileCache.Remove(evictedId);
        }

        // ── Water and miscellaneous assets ────────────────────────────────────────

        private void LoadWaterAndMiscAssets()
        {
            _waterTextures[(int)WaterTextureType.Waterfall]       = TryLoad(_paths.WaterfallTexturePath());
            _waterTextures[(int)WaterTextureType.TopWaterfall]    = TryLoad(_paths.TopWaterfallTexturePath());
            _waterTextures[(int)WaterTextureType.Waterflow]       = TryLoad(_paths.WaterflowTexturePath());
            _waterTextures[(int)WaterTextureType.WaterflowVariantN]  = TryLoad(_paths.WaterflowVariantNPath());
            _waterTextures[(int)WaterTextureType.WaterflowVariantN2] = TryLoad(_paths.WaterflowVariantN2Path());
            _shadowTexture      = TryLoad(_paths.ShadowTexturePath());
            _lightOverlayTexture = TryLoad(_paths.LightOverlayTexturePath());
        }

        // ── Background item LRU cache ─────────────────────────────────────────────

        private void CacheBackgroundItemTexture(int imageId, Texture2D texture)
        {
            if (_bgItemCache.Count >= BG_ITEM_CACHE_CAPACITY)
                EvictLruBgItem();

            _bgItemCache[imageId]    = texture;
            _bgItemLruNodes[imageId] = _bgItemLruList.AddFirst(imageId);
        }

        private void PromoteLruBgItem(int imageId)
        {
            _bgItemLruList.Remove(_bgItemLruNodes[imageId]);
            _bgItemLruNodes[imageId] = _bgItemLruList.AddFirst(imageId);
        }

        private void EvictLruBgItem()
        {
            if (_bgItemLruList.Last == null) return;
            int evictedId = _bgItemLruList.Last.Value;
            _bgItemLruList.RemoveLast();
            _bgItemLruNodes.Remove(evictedId);
            _bgItemCache.Remove(evictedId);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Texture2D TryLoad(string path)
        {
            if (!ResourceLoader.Exists(path))
            {
                GD.PrintErr($"[MapAssetManager] Missing asset: {path}");
                return null;
            }
            return GD.Load<Texture2D>(path);
        }
    }
}