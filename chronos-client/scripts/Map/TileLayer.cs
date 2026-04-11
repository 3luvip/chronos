using Godot;

namespace Map
{
    /// <summary>
    /// Renders the primary tilemap layer.
    /// Handles: standard tiles, waterfall, top-waterfall, tree parallax,
    /// slope tiles (TYPE_DOWN_1_PIXEL), and left/right border columns.
    /// ZOrder = 30 (drawn between background layers and foreground layers).
    /// </summary>
    public sealed class TileLayer : IMapLayer
    {
        public int ZOrder => 30;

        private readonly IMapAssetManager _assetManager;
        private readonly MapAnimClock     _animClock;

        private TileMapData _map;

        public TileLayer(IMapAssetManager assetManager, MapAnimClock animClock)
        {
            _assetManager = assetManager;
            _animClock    = animClock;
        }

        // ── IMapLayer ─────────────────────────────────────────────────────────────

        public void OnMapLoaded(TileMapData map, MapCamera camera) => _map = map;
        public void OnMapUnloaded() => _map = null;
        public void Tick(int animationTick) { /* Stateless — clock is read in Draw. */ }

        public void Draw(CanvasItem canvas, MapCamera camera)
        {
            if (_map == null) return;

            var tileSheet = _assetManager.GetTileSheet(_map.TileSetId);
            DrawVisibleTiles(canvas, camera, tileSheet);
            DrawBorderColumns(canvas, camera, tileSheet);
        }

        // ── Main tile pass ────────────────────────────────────────────────────────

        private void DrawVisibleTiles(CanvasItem canvas, MapCamera camera,
                                      TileSheetInfo? tileSheet)
        {
            for (int tileX = camera.CullTileStartX; tileX < camera.CullTileEndX; tileX++)
            {
                if (IsBorderColumn(tileX)) continue; // drawn separately

                for (int tileY = camera.CullTileStartY; tileY < camera.CullTileEndY; tileY++)
                    DrawSingleTile(canvas, camera, tileSheet, tileX, tileY);
            }
        }

        private void DrawSingleTile(CanvasItem canvas, MapCamera camera,
                                    TileSheetInfo? tileSheet, int tileX, int tileY)
        {
            int typeFlags = _map.TileTypeAt(tileX, tileY);

            if ((typeFlags & TileMapData.TYPE_OUTSIDE) != 0) return;

            int frameIndex = _map.TileFrameAt(tileX, tileY);
            int screenX    = tileX * TileMapData.TILE_SIZE - camera.PositionX;
            int screenY    = tileY * TileMapData.TILE_SIZE - camera.PositionY;

            if ((typeFlags & TileMapData.TYPE_WATERFALL) != 0)
            {
                DrawWaterfallTile(canvas, screenX, screenY);
                return;
            }

            if ((typeFlags & TileMapData.TYPE_TOP_FALL) != 0)
            {
                DrawTopWaterfallTile(canvas, screenX, screenY);
                return;
            }

            // TileSetId 13: background-only tiles are intentionally skipped
            if (_map.TileSetId == 13 && frameIndex >= 0) return;
            if (frameIndex < 0) return;

            if ((typeFlags & TileMapData.TYPE_TREE) != 0)
            {
                DrawTreeParallaxTile(canvas, camera, tileSheet, frameIndex, tileX, tileY);
                return;
            }

            if ((typeFlags & TileMapData.TYPE_DOWN_1_PIXEL) != 0)
            {
                DrawSlopeTile(canvas, tileSheet, frameIndex, screenX, screenY);
                return;
            }

            DrawNormalTile(canvas, tileSheet, frameIndex, screenX, screenY);
        }

        // ── Border column pass ────────────────────────────────────────────────────

        private void DrawBorderColumns(CanvasItem canvas, MapCamera camera,
                                       TileSheetInfo? tileSheet)
        {
            int cameraX = camera.PositionX;
            int cameraY = camera.PositionY;

            // Left border — visible when camera is near the left edge
            if (cameraX < TileMapData.TILE_SIZE)
            {
                for (int tileY = camera.CullTileStartY; tileY < camera.CullTileEndY; tileY++)
                {
                    int frameIndex = _map.TileFrameAt(1, tileY);
                    if (frameIndex < 0) continue;
                    DrawNormalTile(canvas, tileSheet, frameIndex,
                                   -cameraX, tileY * TileMapData.TILE_SIZE - cameraY);
                }
            }

            // Right border — visible when camera has scrolled near the right edge
            if (cameraX > camera.MaxScrollX)
            {
                int rightColumnTileX = _map.TileWidth - 2;
                for (int tileY = camera.CullTileStartY; tileY < camera.CullTileEndY; tileY++)
                {
                    int frameIndex = _map.TileFrameAt(rightColumnTileX, tileY);
                    if (frameIndex < 0) continue;
                    DrawNormalTile(canvas, tileSheet, frameIndex,
                                   (_map.TileWidth - 1) * TileMapData.TILE_SIZE - cameraX,
                                   tileY * TileMapData.TILE_SIZE - cameraY);
                }
            }
        }

        // ── Specialised draw methods ──────────────────────────────────────────────

        private void DrawWaterfallTile(CanvasItem canvas, int screenX, int screenY)
        {
            var texture = _assetManager.GetWaterTexture(WaterTextureType.Waterfall);
            TileDrawHelper.DrawWaterAnimationFrame(canvas, texture, screenX, screenY,
                                                   _animClock.WaterfallFrame);
        }

        private void DrawTopWaterfallTile(CanvasItem canvas, int screenX, int screenY)
        {
            var texture = _assetManager.GetWaterTexture(WaterTextureType.TopWaterfall);
            TileDrawHelper.DrawWaterAnimationFrame(canvas, texture, screenX, screenY,
                                                   _animClock.WaterfallFrame);
        }

        private void DrawTreeParallaxTile(CanvasItem canvas, MapCamera camera,
                                          TileSheetInfo? tileSheet, int frameIndex,
                                          int tileX, int tileY)
        {
            int worldX     = tileX * TileMapData.TILE_SIZE;
            int baseScreenX = worldX - camera.PositionX;
            int deltaFromCenter = baseScreenX - camera.ViewportWidth / 2;
            int parallaxOffset  = (TileMapData.TILE_SIZE - 2) * deltaFromCenter / TileMapData.TILE_SIZE;
            int parallaxScreenX = parallaxOffset + camera.ViewportWidth / 2;
            int screenY        = tileY * TileMapData.TILE_SIZE - camera.PositionY;

            DrawNormalTile(canvas, tileSheet, frameIndex, parallaxScreenX, screenY);
        }

        private void DrawSlopeTile(CanvasItem canvas, TileSheetInfo? tileSheet,
                                   int frameIndex, int screenX, int screenY)
        {
            int tileSize = TileMapData.TILE_SIZE;
            if (tileSheet.HasValue)
            {
                // Draw a 1-pixel cap at the top, then the full tile below it
                TileDrawHelper.DrawTileRegionFromSheet(canvas, tileSheet.Value,
                                                       frameIndex, screenX, screenY, tileSize, 1);
                TileDrawHelper.DrawTileRegionFromSheet(canvas, tileSheet.Value,
                                                       frameIndex, screenX, screenY + 1, tileSize, tileSize);
            }
            else
            {
                var texture = _assetManager.GetTileTexture(_map.TileSetId, frameIndex);
                TileDrawHelper.DrawTileRegionFromTexture(canvas, texture, screenX, screenY, tileSize, 1);
                TileDrawHelper.DrawTileRegionFromTexture(canvas, texture, screenX, screenY + 1, tileSize, tileSize);
            }
        }

        private void DrawNormalTile(CanvasItem canvas, TileSheetInfo? tileSheet,
                                    int frameIndex, int screenX, int screenY)
        {
            if (tileSheet.HasValue)
                TileDrawHelper.DrawTileFromSheet(canvas, tileSheet.Value, frameIndex, screenX, screenY);
            else
            {
                var texture = _assetManager.GetTileTexture(_map.TileSetId, frameIndex);
                TileDrawHelper.DrawTileFromTexture(canvas, texture, screenX, screenY);
            }
        }

        private bool IsBorderColumn(int tileX)
            => tileX == 0 || tileX == _map.TileWidth - 1;
    }
}