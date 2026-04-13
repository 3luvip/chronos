using Chronos.Core.Domain.Map;
using Godot;
using System.Collections.Generic;

namespace Map
{
    // ─────────────────────────────────────────────────────────────────────────────
    // MapRenderer
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Root Node2D for the map rendering system. Thin orchestrator — all render
    /// logic lives in the pipeline and layers.
    ///
    /// Layer draw order (ZOrder):
    ///   10  Far parallax background  (layer 4)
    ///   20  Near background          (layer 1)
    ///   30  Primary tilemap
    ///   35  Water flow overlay
    ///   40  Special overlay          (layer 2)
    ///   [50 Entities — separate node, higher z_index]
    ///   60  Foreground decorations   (layer 3)
    ///   70  Night-light overlay
    ///
    /// Texture filter:
    ///   <c>TextureFilter = Nearest</c> is set on this Node2D in <c>_Ready()</c>.
    ///   All draw calls issued via <c>_Draw()</c> inherit this filter automatically.
    ///   Setting <c>texture_filter</c> on a <see cref="Texture2D"/> resource has
    ///   no effect on CanvasItem draw calls — do not attempt that workaround.
    /// </summary>
    public partial class MapRenderer : Node2D
    {
        // ── Exports ───────────────────────────────────────────────────────────────
        [Export] public string ResourceBasePath = "res://";
        [Export] public bool   EnableLightLayer = true;
        [Export] public bool   EnableWaterLayer = true;

        // ── Core objects ──────────────────────────────────────────────────────────
        private MapRenderPipeline          _pipeline;
        private MapAssetManager            _assetManager;

        private BackgroundParallaxLayer    _farParallaxLayer;
        private BackgroundParallaxLayer    _backgroundLayer;
        private BackgroundParallaxLayer    _overlayLayer;
        private BackgroundParallaxLayer    _foregroundLayer;
        private LightLayer                 _lightLayer;

        // ── Godot lifecycle ───────────────────────────────────────────────────────

        public override void _Ready()
        {
            GlobalPosition = GlobalPosition.Round();
            TextureFilter  = TextureFilterEnum.Nearest; // pixel-perfect rendering

            var paths    = new MapAssetPaths(ResourceBasePath);
            _assetManager = new MapAssetManager(paths);
            _pipeline     = new MapRenderPipeline();

            RegisterAllLayers();

            GetViewport().SizeChanged += OnViewportSizeChanged;
        }

        public override void _Process(double delta)
        {
            _pipeline.Tick();
            QueueRedraw();
        }

        public override void _Draw()
        {
            _pipeline.Draw(this);
        }

        public override void _ExitTree()
        {
            _pipeline.UnloadMap();
            _assetManager.UnloadAll();
            GetViewport().SizeChanged -= OnViewportSizeChanged;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads a new map. Existing assets are released automatically.
        /// Call after parsing <see cref="TileMapData"/> from the server or file.
        /// </summary>
        public void LoadMap(TileMapData map, int characterWorldX, int characterWorldY)
        {
            ClearAllBackgroundItems();
            _assetManager.LoadMapAssets(map);

            var viewportSize = GetViewport().GetVisibleRect().Size;
            _pipeline.LoadMap(map, (int)viewportSize.X, (int)viewportSize.Y,
                              characterWorldX, characterWorldY);
        }

        /// <summary>Adds a single background decoration to the appropriate layer.</summary>
        public void AddBackgroundItem(BackgroundItem item)
        {
            switch (item.Layer)
            {
                case 1: _backgroundLayer.AddItem(item);    break;
                case 2: _overlayLayer.AddItem(item);       break;
                case 3: _foregroundLayer.AddItem(item);    break;
                case 4: _farParallaxLayer.AddItem(item);   break;
            }
        }

        /// <summary>Adds multiple background decorations in one call.</summary>
        public void AddBackgroundItems(IEnumerable<BackgroundItem> items)
        {
            foreach (var item in items) AddBackgroundItem(item);
        }

        /// <summary>Removes all background items. Called automatically before loading a new map.</summary>
        public void ClearAllBackgroundItems()
        {
            _farParallaxLayer?.ClearItems();
            _backgroundLayer?.ClearItems();
            _overlayLayer?.ClearItems();
            _foregroundLayer?.ClearItems();
        }

        /// <summary>
        /// Updates the camera target based on character position and facing direction.
        /// </summary>
        /// <param name="characterWorldX">Character world X in pixels.</param>
        /// <param name="characterWorldY">Character world Y in pixels.</param>
        /// <param name="facingDirection">+1 = right, -1 = left.</param>
        public void UpdateCameraTarget(int characterWorldX, int characterWorldY, int facingDirection)
            => _pipeline.SetCameraTarget(characterWorldX, characterWorldY, facingDirection);

        /// <summary>Enables or disables the night-light overlay at runtime.</summary>
        public void SetLightLayerEnabled(bool enabled)
        {
            if (_lightLayer != null) _lightLayer.IsEnabled = enabled;
        }

        // ── Read-only access for external systems ─────────────────────────────────

        public MapCamera   Camera      => _pipeline.Camera;
        public TileMapData CurrentMap  => _pipeline.CurrentMap;
        public bool        IsMapLoaded => _pipeline.IsReady;

        // ── Private ───────────────────────────────────────────────────────────────

        private void RegisterAllLayers()
        {
            _farParallaxLayer = new BackgroundParallaxLayer(4, zOrder: 10,  _assetManager, new BackgroundItemGrid());
            _backgroundLayer  = new BackgroundParallaxLayer(1, zOrder: 20,  _assetManager, new BackgroundItemGrid());
            var tileLayer      = new TileLayer(_assetManager, _pipeline.AnimClock);
            var waterLayer     = new WaterLayer(_assetManager, _pipeline.AnimClock);
            _overlayLayer      = new BackgroundParallaxLayer(2, zOrder: 40, _assetManager, new BackgroundItemGrid());
            _foregroundLayer   = new BackgroundParallaxLayer(3, zOrder: 60, _assetManager, new BackgroundItemGrid());
            _lightLayer        = new LightLayer(_assetManager);

            _pipeline.RegisterLayer(_farParallaxLayer);
            _pipeline.RegisterLayer(_backgroundLayer);
            _pipeline.RegisterLayer(tileLayer);

            if (EnableWaterLayer)
                _pipeline.RegisterLayer(waterLayer);

            _pipeline.RegisterLayer(_overlayLayer);
            _pipeline.RegisterLayer(_foregroundLayer);

            if (EnableLightLayer)
                _pipeline.RegisterLayer(_lightLayer);
        }

        private void OnViewportSizeChanged()
        {
            var size = GetViewport().GetVisibleRect().Size;
            _pipeline.OnViewportResized((int)size.X, (int)size.Y);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MapDebugOverlay
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Development-only debug overlay Node2D. Attach to the scene during development;
    /// remove before shipping (it contributes no overhead when absent from the tree).
    ///
    /// Displays: FPS, camera world position and scroll target, tile cull range,
    /// visible tile count estimate, and current map metadata.
    /// </summary>
    public partial class MapDebugOverlay : Node2D
    {
        [Export] public NodePath RendererPath;

        private MapRenderer _renderer;
        private Font        _debugFont;

        public override void _Ready()
        {
            if (RendererPath != null)
                _renderer = GetNode<MapRenderer>(RendererPath);

            _debugFont = ThemeDB.FallbackFont;
            ZIndex     = 100;
        }

        public override void _Draw()
        {
            if (_renderer == null || !_renderer.IsMapLoaded || _debugFont == null) return;

            DrawDebugInfo(_renderer.Camera, _renderer.CurrentMap);
        }

        public override void _Process(double delta) => QueueRedraw();

        // ── Private ───────────────────────────────────────────────────────────────

        private void DrawDebugInfo(MapCamera camera, TileMapData map)
        {
            var lines = new[]
            {
                $"FPS: {Engine.GetFramesPerSecond()}",
                $"Camera pos:    ({camera.PositionX}, {camera.PositionY})",
                $"Camera target: ({camera.TargetX}, {camera.TargetY})",
                $"Cull X: {camera.CullTileStartX}–{camera.CullTileEndX}  " +
                $"Y: {camera.CullTileStartY}–{camera.CullTileEndY}",
                $"Visible tiles: ~{(camera.CullTileEndX - camera.CullTileStartX) * (camera.CullTileEndY - camera.CullTileStartY)}",
                $"Map: {map?.TileWidth}×{map?.TileHeight}  TileSetId={map?.TileSetId}",
            };

            float lineY = 16f;
            foreach (var line in lines)
            {
                DrawString(_debugFont, new Vector2(11, lineY + 1), line,
                           HorizontalAlignment.Left, width: -1, fontSize: 13,
                           modulate: new Color(0, 0, 0, 0.7f));
                DrawString(_debugFont, new Vector2(10, lineY), line,
                           HorizontalAlignment.Left, width: -1, fontSize: 13,
                           modulate: Colors.Yellow);
                lineY += 17f;
            }
        }
    }
}