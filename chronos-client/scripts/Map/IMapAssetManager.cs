using Godot;

namespace Map
{
    /// <summary>Identifies which of the five water animation textures to retrieve.</summary>
    public enum WaterTextureType
    {
        Waterfall,
        TopWaterfall,
        Waterflow,
        WaterflowVariantN,
        WaterflowVariantN2
    }

    /// <summary>
    /// Contract for the map asset manager: load, cache, and unload textures.
    /// Completely decoupled from rendering logic, enabling easy test doubles.
    /// </summary>
    public interface IMapAssetManager
    {
        /// <summary>
        /// Retrieves a tile texture by zero-based frame index.
        /// Returns null if the asset is not found or not yet loaded.
        /// </summary>
        Texture2D GetTileTexture(int tileSetId, int frameIndex);

        /// <summary>
        /// Retrieves spritesheet metadata for batch-region rendering.
        /// Returns null when individual-frame mode is active instead.
        /// </summary>
        TileSheetInfo? GetTileSheet(int tileSetId);

        /// <summary>Retrieves the decoration texture for a background item by its image ID.</summary>
        Texture2D GetBackgroundItemTexture(int imageId);

        /// <summary>Retrieves one of the five animated water textures.</summary>
        Texture2D GetWaterTexture(WaterTextureType type);

        /// <summary>Retrieves the shadow overlay texture.</summary>
        Texture2D GetShadowTexture();

        /// <summary>Retrieves the night-light overlay texture.</summary>
        Texture2D GetLightOverlayTexture();

        /// <summary>
        /// Loads all assets required for the given map, unloading any previously
        /// loaded assets first to prevent VRAM leaks.
        /// </summary>
        void LoadMapAssets(TileMapData map);

        /// <summary>Releases all currently held textures. Safe to call multiple times.</summary>
        void UnloadAll();
    }

    /// <summary>
    /// Immutable descriptor for a tile spritesheet, used by
    /// <see cref="TileDrawHelper"/> to extract per-tile regions without
    /// allocating per-frame source rectangles.
    /// </summary>
    public readonly struct TileSheetInfo
    {
        /// <summary>The loaded spritesheet texture.</summary>
        public readonly Texture2D Sheet;

        /// <summary>Number of tile columns in the spritesheet.</summary>
        public readonly int Columns;

        /// <summary>Number of tile rows in the spritesheet.</summary>
        public readonly int Rows;

        /// <summary>Pixel size of each tile in the spritesheet (always square).</summary>
        public readonly int TilePixelSize;

        public TileSheetInfo(Texture2D sheet, int columns, int rows, int tilePixelSize)
        {
            Sheet         = sheet;
            Columns       = columns;
            Rows          = rows;
            TilePixelSize = tilePixelSize;
        }
    }
}