using Godot;
using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Renders one layer of background decoration items (<see cref="BackgroundItem"/>)
    /// with layer-specific parallax scrolling.
    ///
    /// Four instances are created with different ZOrder values:
    ///   Layer 4 (far parallax)  → ZOrder 10
    ///   Layer 1 (background)    → ZOrder 20
    ///   Layer 2 (overlay)       → ZOrder 40
    ///   Layer 3 (foreground)    → ZOrder 60
    ///
    /// Items are added via <see cref="AddItem"/> after the map loads.
    /// The spatial index is rebuilt lazily when items change.
    /// </summary>
    public sealed class BackgroundParallaxLayer : IMapLayer
    {
        public int ZOrder { get; }

        private const int LINEAR_SCAN_THRESHOLD = 50;

        private readonly int _layerIndex;
        private readonly IMapAssetManager _assetManager;
        private readonly IBackgroundItemSpatialIndex _spatialIndex;

        private readonly List<BackgroundItem>         _items              = new();
        private readonly List<BackgroundItem>         _spatialQueryBuffer = new(128);

        private TileMapData _map;
        private bool        _indexNeedsRebuild = true;

        // ── Special image IDs ─────────────────────────────────────────────────────
        private const int WATER_OVERLAY_IMAGE_ID          = 11;
        private const int WATER_OVERLAY_SKIP_MAP_ID       = 122;
        private const int WATER_OVERLAY_ANIMATION_INTERVAL_MS = 125;
        private const int WATER_OVERLAY_TILE_COUNT        = 2;

        public BackgroundParallaxLayer(int layerIndex, int zOrder,
                                       IMapAssetManager assetManager,
                                       IBackgroundItemSpatialIndex spatialIndex)
        {
            _layerIndex   = layerIndex;
            ZOrder        = zOrder;
            _assetManager = assetManager;
            _spatialIndex = spatialIndex;
        }

        // ── Item management ───────────────────────────────────────────────────────

        public void AddItem(BackgroundItem item)
        {
            _items.Add(item);
            _indexNeedsRebuild = true;
        }

        public void ClearItems()
        {
            _items.Clear();
            _spatialIndex.Clear();
            _indexNeedsRebuild = false;
        }

        // ── IMapLayer ─────────────────────────────────────────────────────────────

        public void OnMapLoaded(TileMapData map, MapCamera camera)
        {
            _map = map;
            // Items are added after this call via AddItem() — do not clear here.
        }

        public void OnMapUnloaded()
        {
            ClearItems();
            _map = null;
        }

        public void Tick(int animationTick)
        {
            if (_indexNeedsRebuild && _items.Count > 0)
            {
                _spatialIndex.Build(_items);
                _indexNeedsRebuild = false;
            }
        }

        public void Draw(CanvasItem canvas, MapCamera camera)
        {
            if (_map == null) return;

            var drawList = GetItemsToRender(camera);

            foreach (var item in drawList)
            {
                var texture = _assetManager.GetBackgroundItemTexture(item.ImageId);
                if (texture == null) continue;

                int imageWidth  = texture.GetWidth();
                int imageHeight = texture.GetHeight();

                if (!item.IsVisibleInViewport(camera.PositionX, camera.PositionY,
                                              camera.ViewportWidth, camera.ViewportHeight,
                                              imageWidth, imageHeight)) continue;

                var worldPos = item.GetParallaxWorldPos(camera.PositionX, camera.PositionY);
                int screenX  = worldPos.X - camera.PositionX;
                int screenY  = worldPos.Y - camera.PositionY;

                if (item.Transform == 2)
                    TileDrawHelper.DrawDecorationTextureFlippedHorizontal(canvas, texture, screenX, screenY);
                else
                    TileDrawHelper.DrawDecorationTexture(canvas, texture, screenX, screenY);

                DrawWaterOverlayIfNeeded(canvas, camera, item, worldPos);
                DrawMirrorIfNeeded(canvas, camera, item, texture, worldPos, imageWidth, imageHeight);
            }
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private IReadOnlyList<BackgroundItem> GetItemsToRender(MapCamera camera)
        {
            // Layer 4 uses a position offset that invalidates spatial index queries.
            // For small item counts, linear scan is also faster than index overhead.
            bool useSpatialIndex = _layerIndex != 4 && _items.Count > LINEAR_SCAN_THRESHOLD;

            if (!useSpatialIndex) return _items;

            _spatialIndex.Query(camera.PositionX, camera.PositionY,
                                camera.ViewportWidth, camera.ViewportHeight,
                                _spatialQueryBuffer);
            return _spatialQueryBuffer;
        }

        /// <summary>
        /// Image ID 11 has a water-flow overlay drawn above it.
        /// Skip on map 122 which uses a different visual treatment.
        /// </summary>
        private void DrawWaterOverlayIfNeeded(CanvasItem canvas, MapCamera camera,
                                              BackgroundItem item, Vector2I worldPos)
        {
            if (item.ImageId != WATER_OVERLAY_IMAGE_ID) return;
            if (_map.MapId == WATER_OVERLAY_SKIP_MAP_ID) return;

            var waterflowTexture = _assetManager.GetWaterTexture(WaterTextureType.Waterflow);
            if (waterflowTexture == null) return;

            // Use wall-clock time rather than the game tick so this animation
            // doesn't depend on a MapAnimClock reference being passed in.
            int animationFrame = (int)(Time.GetTicksMsec() / WATER_OVERLAY_ANIMATION_INTERVAL_MS) % 2;
            int screenX        = worldPos.X - camera.PositionX;
            int screenY        = worldPos.Y - camera.PositionY;
            int tileSize       = TileDrawHelper.DISPLAY_TILE_SIZE;

            for (int tileIndex = 0; tileIndex < WATER_OVERLAY_TILE_COUNT; tileIndex++)
            {
                var sourceRect = new Rect2(0, animationFrame * tileSize, tileSize, tileSize);
                var destRect   = new Rect2(screenX + tileIndex * tileSize, screenY + tileSize,
                                           tileSize, tileSize);
                canvas.DrawTextureRectRegion(waterflowTexture, destRect, sourceRect);
            }
        }

        /// <summary>
        /// In double maps, eligible items are also drawn mirrored at the far end of the map.
        /// </summary>
        private void DrawMirrorIfNeeded(CanvasItem canvas, MapCamera camera,
                                        BackgroundItem item, Texture2D texture,
                                        Vector2I worldPos, int imageWidth, int imageHeight)
        {
            if (!_map.IsDoubleMap() || !item.ShouldMirrorInDoubleMap) return;

            int mirroredWorldX  = _map.PixelWidth - (worldPos.X + imageWidth);
            int mirroredScreenX = mirroredWorldX - camera.PositionX;
            int screenY         = worldPos.Y - camera.PositionY;

            bool isRightOfViewport = mirroredWorldX > camera.PositionX + camera.ViewportWidth;
            bool isLeftOfViewport  = mirroredWorldX + imageWidth < camera.PositionX;
            bool isAboveViewport   = worldPos.Y + imageHeight < camera.PositionY;
            bool isBelowViewport   = worldPos.Y > camera.PositionY + camera.ViewportHeight;

            if (isRightOfViewport || isLeftOfViewport || isAboveViewport || isBelowViewport) return;

            TileDrawHelper.DrawDecorationTextureFlippedHorizontal(canvas, texture, mirroredScreenX, screenY);
        }
    }
}