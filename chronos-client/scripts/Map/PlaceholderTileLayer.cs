using Godot;

namespace Map
{
    /// <summary>
    /// Debug-only tile layer that renders colored rectangles instead of real textures.
    /// Use in place of <see cref="TileLayer"/> during development to verify map
    /// structure without requiring production assets.
    ///
    /// Remove from the scene before shipping — this layer has no release path guard
    /// because the pipeline's layer registration is the switch point.
    ///
    /// Color scheme by raw frame value:
    ///   0   = transparent (skip)
    ///   39  = dark gray (boundary)
    ///   9   = tan (ground top edge)
    ///   1,2,3 = green shades (surface)
    ///   4,8   = brown (left/right cap)
    ///   5,6,7 = earth shades (underground fill)
    /// </summary>
    public sealed class PlaceholderTileLayer : IMapLayer
    {
        public int ZOrder => 30;

        private TileMapData _map;

        // ── IMapLayer ─────────────────────────────────────────────────────────────

        public void OnMapLoaded(TileMapData map, MapCamera camera) => _map = map;
        public void OnMapUnloaded() => _map = null;
        public void Tick(int animationTick) { }

        public void Draw(CanvasItem canvas, MapCamera camera)
        {
            if (_map == null) return;

            DrawTiles(canvas, camera);
            DrawGridOverlay(canvas, camera);
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void DrawTiles(CanvasItem canvas, MapCamera camera)
        {
            int tileSize = TileMapData.TILE_SIZE;

            for (int tileX = camera.CullTileStartX; tileX < camera.CullTileEndX; tileX++)
            for (int tileY = camera.CullTileStartY; tileY < camera.CullTileEndY; tileY++)
            {
                int rawFrame = _map.TileFrames[tileY * _map.TileWidth + tileX];
                if (rawFrame == 0) continue;

                int typeFlags = _map.TileTypeAt(tileX, tileY);
                if ((typeFlags & TileMapData.TYPE_OUTSIDE) != 0) continue;

                int screenX = tileX * tileSize - camera.PositionX;
                int screenY = tileY * tileSize - camera.PositionY;
                var tileRect = new Rect2(screenX, screenY, tileSize, tileSize);

                canvas.DrawRect(tileRect, TileDebugColor(rawFrame));
                canvas.DrawRect(tileRect, new Color(0, 0, 0, 0.15f), filled: false, width: 0.5f);

                DrawFrameLabel(canvas, rawFrame, screenX, screenY, tileSize);
            }
        }

        private static void DrawFrameLabel(CanvasItem canvas, int rawFrame,
                                           int screenX, int screenY, int tileSize)
        {
            if (tileSize < 20) return;

            var font = ThemeDB.FallbackFont;
            if (font == null) return;

            canvas.DrawString(font,
                              new Vector2(screenX + 2, screenY + tileSize - 3),
                              rawFrame.ToString(),
                              HorizontalAlignment.Left,
                              width: -1, fontSize: 9,
                              modulate: new Color(1, 1, 1, 0.6f));
        }

        private static void DrawGridOverlay(CanvasItem canvas, MapCamera camera)
        {
            int tileSize   = TileMapData.TILE_SIZE;
            var gridColor  = new Color(0.5f, 0.5f, 0.5f, 0.08f);

            int startX = (camera.PositionX / tileSize) * tileSize - camera.PositionX;
            for (int x = startX; x < camera.ViewportWidth + tileSize; x += tileSize)
                canvas.DrawLine(new Vector2(x, 0), new Vector2(x, camera.ViewportHeight), gridColor, 0.5f);

            int startY = (camera.PositionY / tileSize) * tileSize - camera.PositionY;
            for (int y = startY; y < camera.ViewportHeight + tileSize; y += tileSize)
                canvas.DrawLine(new Vector2(0, y), new Vector2(camera.ViewportWidth, y), gridColor, 0.5f);
        }

        private static Color TileDebugColor(int rawFrame) => rawFrame switch
        {
            39  => new Color(0.25f, 0.25f, 0.25f),   // boundary — dark gray
            9   => new Color(0.55f, 0.40f, 0.20f),   // top edge — tan
            1   => new Color(0.30f, 0.60f, 0.20f),   // surface A — green
            2   => new Color(0.25f, 0.55f, 0.18f),   // surface B — lighter green
            3   => new Color(0.20f, 0.50f, 0.15f),   // surface C — darker green
            4   => new Color(0.45f, 0.30f, 0.15f),   // left cap — brown
            8   => new Color(0.45f, 0.30f, 0.15f),   // right cap — brown
            5   => new Color(0.38f, 0.25f, 0.12f),   // fill A — earth
            6   => new Color(0.33f, 0.22f, 0.10f),   // fill B — lighter earth
            7   => new Color(0.30f, 0.20f, 0.09f),   // fill C — dark earth
            475 => new Color(0.20f, 0.40f, 0.60f),   // special — blue
            _   => new Color(1f, 0f, 1f, 0.5f)       // unrecognised — magenta
        };
    }
}