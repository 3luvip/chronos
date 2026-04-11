using Godot;
using System;
using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Orchestrates the full map render pipeline.
    /// Layers are registered once via <see cref="RegisterLayer"/> and sorted by
    /// <see cref="IMapLayer.ZOrder"/> — this class never needs modification when
    /// new layers are added (Open/Closed Principle).
    ///
    /// Responsibilities:
    ///   - Layer lifecycle (load / unload notifications)
    ///   - Per-frame tick and draw dispatch
    ///   - Camera target management from external character controllers
    ///   - Viewport resize handling
    /// </summary>
    public sealed class MapRenderPipeline
    {
        private readonly List<IMapLayer> _layers    = new();
        private readonly MapAnimClock    _animClock = new();
        private readonly MapCamera       _camera    = new();

        private TileMapData _currentMap;
        private int         _viewportWidth;
        private int         _viewportHeight;
        private bool        _isReady;

        // ── Layer registration ────────────────────────────────────────────────────

        /// <summary>
        /// Registers a layer into the pipeline. Automatically re-sorts by ZOrder.
        /// Must be called before <see cref="LoadMap"/>.
        /// </summary>
        public void RegisterLayer(IMapLayer layer)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            _layers.Add(layer);
            _layers.Sort(static (a, b) => a.ZOrder.CompareTo(b.ZOrder));
        }

        // ── Map lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Loads a new map, notifying all registered layers.
        /// Automatically unloads any previously active map first.
        /// </summary>
        /// <param name="map">Validated, fully-loaded map data.</param>
        /// <param name="viewportWidth">Current viewport width in pixels.</param>
        /// <param name="viewportHeight">Current viewport height in pixels.</param>
        /// <param name="startWorldX">Character world X used to position the initial camera.</param>
        /// <param name="startWorldY">Character world Y used to position the initial camera.</param>
        public void LoadMap(TileMapData map, int viewportWidth, int viewportHeight,
                            int startWorldX, int startWorldY)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            if (_currentMap != null) UnloadMap();

            _currentMap     = map;
            _viewportWidth  = viewportWidth;
            _viewportHeight = viewportHeight;

            _animClock.Reset();
            _camera.Initialize(map, viewportWidth, viewportHeight, startWorldX, startWorldY);

            foreach (var layer in _layers)
                layer.OnMapLoaded(map, _camera);

            _isReady = true;
        }

        /// <summary>Unloads the current map and notifies all layers.</summary>
        public void UnloadMap()
        {
            foreach (var layer in _layers)
                layer.OnMapUnloaded();

            _currentMap = null;
            _isReady    = false;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────────

        /// <summary>Advances the animation clock and camera, then ticks all layers. Call in _Process.</summary>
        public void Tick()
        {
            if (!_isReady) return;

            _animClock.Advance();
            _camera.Update(_currentMap);

            foreach (var layer in _layers)
                layer.Tick(_animClock.CurrentTick);
        }

        /// <summary>Dispatches draw calls to all layers in ZOrder. Call in _Draw.</summary>
        public void Draw(CanvasItem canvas)
        {
            if (!_isReady) return;

            foreach (var layer in _layers)
                layer.Draw(canvas, _camera);
        }

        // ── Camera API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the camera target based on the character's current world position.
        /// The camera leads slightly in the direction of movement.
        /// </summary>
        /// <param name="characterWorldX">Character world X in pixels.</param>
        /// <param name="characterWorldY">Character world Y in pixels.</param>
        /// <param name="characterDirection">Movement direction: +1 = right, -1 = left.</param>
        public void SetCameraTarget(int characterWorldX, int characterWorldY, int characterDirection)
        {
            if (!_isReady) return;

            int horizontalLead = (_viewportWidth  / 6) * characterDirection;
            int targetX        = characterWorldX - _viewportWidth  / 2 + horizontalLead;
            int targetY        = characterWorldY - _viewportHeight * 2 / 3;

            // Clamp before SetTarget as an extra guard against invalid state
            targetX = Math.Max(targetX, 0);
            targetY = Math.Max(targetY, 0);

            _camera.SetTarget(targetX, targetY);
        }

        /// <summary>Teleports the camera instantly to the given world position.</summary>
        public void SetCameraInstant(int worldX, int worldY)
            => _camera.SetCameraInstant(worldX, worldY);

        /// <summary>Recalculates camera limits after a viewport orientation change.</summary>
        public void OnViewportResized(int newWidth, int newHeight)
        {
            _viewportWidth  = newWidth;
            _viewportHeight = newHeight;
            if (_isReady) _camera.OnViewportResized(_currentMap, newWidth, newHeight);
        }

        // ── Read-only access ──────────────────────────────────────────────────────

        public MapCamera    Camera     => _camera;
        public MapAnimClock AnimClock  => _animClock;
        public bool         IsReady    => _isReady;
        public TileMapData  CurrentMap => _currentMap;
    }
}