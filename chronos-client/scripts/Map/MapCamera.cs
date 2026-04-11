using Godot;
using System;

namespace Map
{
    /// <summary>
    /// Manages the camera's world-space position and the visible tile culling range.
    /// Pure logic class with no Godot Node dependencies — trivially unit-testable.
    /// Camera scrolls toward <see cref="TargetX"/>/<see cref="TargetY"/> using a
    /// fixed-point accumulator to achieve smooth sub-pixel motion without floats.
    /// </summary>
    public sealed class MapCamera
    {
        // ── World position (pixels) ───────────────────────────────────────────────

        /// <summary>Left edge of the camera viewport in world pixels.</summary>
        public int PositionX { get; private set; }

        /// <summary>Top edge of the camera viewport in world pixels.</summary>
        public int PositionY { get; private set; }

        /// <summary>Scroll target X — camera moves toward this each frame.</summary>
        public int TargetX { get; private set; }

        /// <summary>Scroll target Y — camera moves toward this each frame.</summary>
        public int TargetY { get; private set; }

        // ── Sub-pixel scroll accumulators (fixed-point, 4 fractional bits) ───────
        private int _subPixelAccumulatorX;
        private int _subPixelAccumulatorY;

        // ── Scroll limits (maximum allowed PositionX / PositionY) ─────────────────
        /// <summary>Maximum camera X in world pixels (clamp guard for narrow maps).</summary>
        public int MaxScrollX { get; private set; }

        /// <summary>Maximum camera Y in world pixels (clamp guard for short maps).</summary>
        public int MaxScrollY { get; private set; }

        // ── Viewport dimensions (pixels) ──────────────────────────────────────────
        public int ViewportWidth  { get; private set; }
        public int ViewportHeight { get; private set; }

        // ── Tile culling range (half-open intervals [start, end)) ─────────────────
        public int CullTileStartX { get; private set; }
        public int CullTileEndX   { get; private set; }
        public int CullTileStartY { get; private set; }
        public int CullTileEndY   { get; private set; }

        // Minimum X to keep the left border column always visible
        private const int MIN_CAMERA_X = 24;

        // ── Initialisation ────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the camera for a newly loaded map.
        /// </summary>
        /// <param name="map">The map data (provides pixel and tile dimensions).</param>
        /// <param name="viewportWidth">Viewport width in pixels.</param>
        /// <param name="viewportHeight">Viewport height in pixels.</param>
        /// <param name="startWorldX">Character world X to centre the camera on.</param>
        /// <param name="startWorldY">Character world Y to target the camera at.</param>
        public void Initialize(TileMapData map, int viewportWidth, int viewportHeight,
                               int startWorldX, int startWorldY)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            ViewportWidth  = viewportWidth;
            ViewportHeight = viewportHeight;
            RecalculateLimits(map);

            int targetX = startWorldX - viewportWidth  / 2 + viewportWidth  / 6;
            int targetY = startWorldY - viewportHeight * 2 / 3;
            SetCameraInstant(targetX, targetY);
            UpdateCullRange(map);
        }

        // ── Per-frame update ──────────────────────────────────────────────────────

        /// <summary>
        /// Smoothly advances the camera one frame toward its target.
        /// Call once per <c>_Process</c> tick.
        /// </summary>
        public void Update(TileMapData map)
        {
            if (map == null) return;
            ScrollTowardTarget();
            UpdateCullRange(map);
        }

        // ── Target API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the scroll target, clamped to valid scroll bounds.
        /// Guards against crash when map is smaller than viewport (MaxScrollX &lt; MIN_CAMERA_X).
        /// </summary>
        public void SetTarget(int worldX, int worldY)
        {
            int clampedMaxX = Math.Max(MIN_CAMERA_X, MaxScrollX);
            int clampedMaxY = Math.Max(0, MaxScrollY);
            TargetX = Math.Clamp(worldX, MIN_CAMERA_X, clampedMaxX);
            TargetY = Math.Clamp(worldY, 0, clampedMaxY);
        }

        /// <summary>Teleports the camera instantly, skipping smooth scrolling.</summary>
        public void SetCameraInstant(int worldX, int worldY)
        {
            PositionX = Math.Clamp(worldX, MIN_CAMERA_X, Math.Max(MIN_CAMERA_X, MaxScrollX));
            PositionY = Math.Clamp(worldY, 0, Math.Max(0, MaxScrollY));
            TargetX   = PositionX;
            TargetY   = PositionY;
            _subPixelAccumulatorX = 0;
            _subPixelAccumulatorY = 0;
        }

        // ── Viewport resize ───────────────────────────────────────────────────────

        /// <summary>Recalculates limits and re-clamps the camera after an orientation change.</summary>
        public void OnViewportResized(TileMapData map, int newViewportWidth, int newViewportHeight)
        {
            ViewportWidth  = newViewportWidth;
            ViewportHeight = newViewportHeight;
            RecalculateLimits(map);
            ClampCameraPosition();
            UpdateCullRange(map);
        }

        // ── Coordinate helpers ────────────────────────────────────────────────────

        /// <summary>Converts a world-space point to screen-space.</summary>
        public Vector2 WorldToScreen(float worldX, float worldY)
            => new Vector2(worldX - PositionX, worldY - PositionY);

        /// <summary>Converts a screen-space point to world-space.</summary>
        public Vector2I ScreenToWorld(int screenX, int screenY)
            => new Vector2I(screenX + PositionX, screenY + PositionY);

        /// <summary>Returns true if the AABB is at least partially visible in the viewport.</summary>
        public bool IsVisible(int worldX, int worldY, int width, int height)
            => worldX + width  >= PositionX && worldX <= PositionX + ViewportWidth  &&
               worldY + height >= PositionY && worldY <= PositionY + ViewportHeight;

        // ── Private helpers ───────────────────────────────────────────────────────

        private void RecalculateLimits(TileMapData map)
        {
            int tileSize = TileMapData.TILE_SIZE;
            MaxScrollX = Math.Max(MIN_CAMERA_X, (map.TileWidth  - 1) * tileSize - ViewportWidth);
            MaxScrollY = Math.Max(0,             (map.TileHeight - 1) * tileSize - ViewportHeight);
        }

        private void ScrollTowardTarget()
        {
            if (PositionX == TargetX && PositionY == TargetY) return;

            _subPixelAccumulatorX += (TargetX - PositionX) * 4;
            _subPixelAccumulatorY += (TargetY - PositionY) * 4;
            PositionX             += _subPixelAccumulatorX >> 4;
            PositionY             += _subPixelAccumulatorY >> 4;
            _subPixelAccumulatorX &= 15;
            _subPixelAccumulatorY &= 15;
            ClampCameraPosition();
        }

        private void ClampCameraPosition()
        {
            PositionX = Math.Clamp(PositionX, MIN_CAMERA_X, Math.Max(MIN_CAMERA_X, MaxScrollX));
            PositionY = Math.Clamp(PositionY, 0,            Math.Max(0,             MaxScrollY));
        }

        private void UpdateCullRange(TileMapData map)
        {
            int tileSize = TileMapData.TILE_SIZE;
            CullTileStartX = Math.Max(0, PositionX / tileSize - 1);
            CullTileStartY = Math.Max(0, PositionY / tileSize);
            CullTileEndX   = Math.Min(map.TileWidth,  CullTileStartX + ViewportWidth  / tileSize + 2);
            CullTileEndY   = Math.Min(map.TileHeight, CullTileStartY + ViewportHeight / tileSize + 2);
        }
    }
}