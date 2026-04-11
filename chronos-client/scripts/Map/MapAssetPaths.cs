using System;

namespace Map
{
    /// <summary>
    /// Centralised registry of every asset path used by the Map module.
    /// Pure C# — no Godot API dependency. Changing the folder structure
    /// only requires edits here.
    /// </summary>
    public sealed class MapAssetPaths
    {
        public string BasePath { get; }

        /// <param name="basePath">
        /// Root resource path, e.g. <c>"res://"</c>.
        /// A trailing slash is added automatically if absent.
        /// </param>
        public MapAssetPaths(string basePath = "res://")
        {
            BasePath = NormalizeBasePath(basePath);
        }

        // ── Tile textures ─────────────────────────────────────────────────────────

        /// <summary>
        /// Path to a single spritesheet containing all frames for a tile set.
        /// Example: <c>res://t/3.png</c>
        /// </summary>
        public string TileSpritesheetPath(int tileSetId)
            => $"{BasePath}t/{tileSetId}.png";

        /// <summary>
        /// Path to an individual frame inside a per-tile-set subdirectory.
        /// Example: <c>res://t/3/t_01.png</c>, <c>res://t/3/t_12.png</c>
        /// </summary>
        public string TileSubdirectoryFramePath(int tileSetId, int oneBasedFrameIndex)
        {
            int safeIndex = Math.Max(1, oneBasedFrameIndex);
            string paddedIndex = safeIndex <= 9
                ? $"0{safeIndex}"
                : safeIndex.ToString();
            return $"{BasePath}t/{tileSetId}/t_{paddedIndex}.png";
        }

        /// <summary>
        /// Path to a standalone tile PNG identified by its raw frame ID.
        /// Example: <c>res://t/475.png</c>
        /// </summary>
        public string TileDirectFramePath(int frameId)
            => $"{BasePath}t/{frameId}.png";

        // ── Background decoration items ───────────────────────────────────────────

        /// <summary>Example: <c>res://mapBackGround/42.png</c></summary>
        public string BackgroundItemPath(int imageId)
            => $"{BasePath}mapBackGround/{imageId}.png";

        // ── Water textures ────────────────────────────────────────────────────────

        public string WaterfallTexturePath()      => $"{BasePath}tWater/wtf.png";
        public string TopWaterfallTexturePath()   => $"{BasePath}tWater/twtf.png";
        public string WaterflowTexturePath()      => $"{BasePath}tWater/wts.png";
        public string WaterflowVariantNPath()     => $"{BasePath}tWater/wtsN.png";
        public string WaterflowVariantN2Path()    => $"{BasePath}tWater/wtsN2.png";

        // ── Miscellaneous ─────────────────────────────────────────────────────────

        public string ShadowTexturePath()         => $"{BasePath}mainImage/shadowBig.png";
        public string LightOverlayTexturePath()   => $"{BasePath}bg/light.png";

        // ── Private ───────────────────────────────────────────────────────────────

        private static string NormalizeBasePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "res://";
            path = path.Trim();
            if (!path.EndsWith("/", StringComparison.Ordinal)) path += "/";
            return path;
        }
    }
}