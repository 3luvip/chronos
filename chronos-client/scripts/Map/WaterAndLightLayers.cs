using Chronos.Core.Domain.Map;
using Godot;

namespace Map
{
    // ─────────────────────────────────────────────────────────────────────────────
    // WaterLayer
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders animated water-flow overlay tiles on top of the primary tilemap.
    /// ZOrder = 35 (immediately above <see cref="TileLayer"/>).
    ///
    /// Also exposes <see cref="WaterSurfaceWorldY"/> for other systems (e.g. swimming
    /// particles or background item anchoring) that need the water-surface Y.
    /// </summary>
    public sealed class WaterLayer : IMapLayer
    {
        public int ZOrder => 35;

        private readonly IMapAssetManager _assetManager;
        private readonly MapAnimClock     _animClock;

        private TileMapData _map;

        /// <summary>
        /// World-space Y coordinate of the water surface, set during <see cref="Draw"/>.
        /// Zero when no waterflow tile has been encountered yet, or after unload.
        /// </summary>
        public int WaterSurfaceWorldY { get; private set; }

        public WaterLayer(IMapAssetManager assetManager, MapAnimClock animClock)
        {
            _assetManager = assetManager;
            _animClock    = animClock;
        }

        // ── IMapLayer ─────────────────────────────────────────────────────────────

        public void OnMapLoaded(TileMapData map, MapCamera camera)
        {
            _map                 = map;
            WaterSurfaceWorldY   = 0;
        }

        public void OnMapUnloaded()
        {
            _map               = null;
            WaterSurfaceWorldY = 0;
        }

        public void Tick(int animationTick) { /* Surface Y is recalculated in Draw each frame. */ }

        public void Draw(CanvasItem canvas, MapCamera camera)
        {
            if (_map == null) return;

            var waterflowTexture = GetWaterflowTextureForCurrentMap();
            if (waterflowTexture == null) return;

            WaterSurfaceWorldY = 0; // reset; will be assigned on first visible waterflow tile

            for (int tileX = camera.CullTileStartX; tileX < camera.CullTileEndX; tileX++)
            for (int tileY = camera.CullTileStartY; tileY < camera.CullTileEndY; tileY++)
            {
                if ((_map.TileTypeAt(tileX, tileY) & TileMapData.TypeWaterflow) == 0) continue;
                DrawWaterflowTile(canvas, camera, waterflowTexture, tileX, tileY);
            }
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void DrawWaterflowTile(CanvasItem canvas, MapCamera camera,
                                       Texture2D waterflowTexture, int tileX, int tileY)
        {
            const int ANIMATED_FRAME_RISE_PX = 12;

            int screenX = tileX * TileMapData.TileSize - camera.PositionX;
            int screenY = tileY * TileMapData.TileSize - camera.PositionY;

            if (!_map.HasWaterEffect())
            {
                // Static maps display two fixed frames stacked for a non-animated effect
                TileDrawHelper.DrawWaterAnimationFrame(canvas, waterflowTexture, screenX, screenY - 1, 0);
                TileDrawHelper.DrawWaterAnimationFrame(canvas, waterflowTexture, screenX, screenY - 3, 0);
            }

            TileDrawHelper.DrawWaterAnimationFrame(canvas, waterflowTexture,
                                                   screenX, screenY - ANIMATED_FRAME_RISE_PX,
                                                   _animClock.WaterflowFrame);

            if (WaterSurfaceWorldY == 0 && _map.HasWaterEffect())
                WaterSurfaceWorldY = tileY * TileMapData.TileSize - ANIMATED_FRAME_RISE_PX;
        }

        private Texture2D GetWaterflowTextureForCurrentMap()
        {
            if (_map == null) return null;
            return _map.TileSetId switch
            {
                5 => _assetManager.GetWaterTexture(WaterTextureType.WaterflowVariantN),
                8 => _assetManager.GetWaterTexture(WaterTextureType.WaterflowVariantN2),
                _ => _assetManager.GetWaterTexture(WaterTextureType.Waterflow)
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LightLayer
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a tiled night-light overlay across the viewport.
    /// Tiles scroll horizontally at half the camera speed (parallax effect).
    /// ZOrder = 70 (topmost layer, above all content including foreground).
    /// </summary>
    public sealed class LightLayer : IMapLayer
    {
        public int ZOrder => 70;

        private const int LIGHT_TILE_SPACING_EXTRA_PX = 50;
        private const int LIGHT_TILE_ORIGIN_OFFSET_X  = 100;
        private const int LIGHT_TILE_PARALLAX_DIVISOR = 2;
        private const int LIGHT_TILE_VERTICAL_OFFSET  = -20;

        private readonly IMapAssetManager _assetManager;
        private TileMapData _map;

        /// <summary>When false, the layer produces no draw calls.</summary>
        public bool IsEnabled { get; set; } = true;

        public LightLayer(IMapAssetManager assetManager)
        {
            _assetManager = assetManager;
        }

        // ── IMapLayer ─────────────────────────────────────────────────────────────

        public void OnMapLoaded(TileMapData map, MapCamera camera) => _map = map;
        public void OnMapUnloaded() => _map = null;
        public void Tick(int animationTick) { }

        public void Draw(CanvasItem canvas, MapCamera camera)
        {
            if (!IsEnabled || _map == null) return;

            var lightTexture = _assetManager.GetLightOverlayTexture();
            if (lightTexture == null) return;

            int tileWidth = lightTexture.GetWidth();
            int spacing   = tileWidth + LIGHT_TILE_SPACING_EXTRA_PX;
            int tileCount = _map.PixelWidth / spacing + 1;

            for (int index = 0; index < tileCount; index++)
            {
                int screenX = LIGHT_TILE_ORIGIN_OFFSET_X
                              + index * spacing
                              - camera.PositionX / LIGHT_TILE_PARALLAX_DIVISOR;

                int screenY = LIGHT_TILE_VERTICAL_OFFSET - camera.PositionY;

                bool isRightOfViewport  = screenX > camera.ViewportWidth;
                bool isLeftOfViewport   = screenX + tileWidth < 0;
                if (isRightOfViewport || isLeftOfViewport) continue;

                TileDrawHelper.DrawDecorationTexture(canvas, lightTexture, screenX, screenY);
            }
        }
    }
}