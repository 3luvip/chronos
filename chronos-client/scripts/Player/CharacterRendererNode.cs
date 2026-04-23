using Godot;
using System;
using System.Collections.Generic;
using Map;
using Chronos.Core.Domain.Character;
namespace Player
{
    // 2. CHARACTER RENDERER NODE — Godot Node2D, reads SpriteDrawCall[] and draws
    //    Replaces the entire paintCharBody() → SmallImage.drawSmallImage() chain
    // ─────────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// One Node2D per character scene. Lives at z_index = 50 (between map layers).
    /// _Draw() is called by Godot after Tick() resolves all draw calls.
    ///
    /// Rendering path:
    ///   CharacterModel.Anim.Tick()
    ///   → AnimationFrame = Anim.GetCurrentFrame()
    ///   → SpriteResolver.Resolve() → List&lt;SpriteDrawCall&gt;
    ///   → RenderLayerStack.Sort()
    ///   → _Draw() → canvas.DrawTextureRectRegion() per call
    /// </summary>
    public partial class CharacterRendererNode : Node2D
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        public CharacterModel       Model    { get; set; }
        public SpriteAtlasLoader    Atlas    { get; set; }
        public MapRenderer          MapRef   { get; set; }
        public IPartRegistry Registry { get; set; }
 
        // ── Per-frame resolved draw calls ─────────────────────────────────────
        private readonly List<SpriteDrawCall> _resolvedCalls = new(32);
        private readonly RenderLayerStack     _layerStack    = new();
 
        // ── Constants ─────────────────────────────────────────────────────────
        private const int DISPLAY_TILE_SIZE = 32;
 
        // ── Godot lifecycle ───────────────────────────────────────────────────
 
        public override void _Ready()
        {
            ZIndex = 50;
        }
 
        public override void _Process(double delta)
        {
            if (Model == null || MapRef == null || !MapRef.IsMapLoaded) return;
 
            float dt = (float)delta;
 
            // 1. Advance animation
            Model.Anim.Tick(dt);
 
            // 2. Map status → anim state
            var targetState = CharacterStatusMapper.ToAnimState(Model.Status);
            Model.Anim.RequestTransition(targetState);
 
            // 3. Resolve draw calls
            var frame = Model.Anim.GetCurrentFrame();
            if (frame != null)
            {
                _resolvedCalls.Clear();
 
                var resolver = new SpriteResolver(
                    GetPartRegistry(),
                    MapRef.Camera.PositionX,
                    MapRef.Camera.PositionY);
 
                resolver.Resolve(Model, frame, _resolvedCalls);
 
                _layerStack.Clear();
                _layerStack.AddRange(_resolvedCalls);
                _layerStack.Sort();
            }
 
            QueueRedraw();
        }
 
        public override void _Draw()
        {
            if (Atlas == null) return;
 
            foreach (var call in _layerStack.Sorted)
            {
                var entry = Atlas.GetSprite(call.SpriteId);
                if (!entry.IsValid) continue;
 
                int w = (int)entry.SourceRect.Size.X;
                int h = (int)entry.SourceRect.Size.Y;
 
                if (call.FlipH)
                {
                    DrawSetTransformMatrix(
                        new Transform2D(-1, 0, 0, 1, call.ScreenX + w, call.ScreenY));
                    DrawTextureRectRegion(entry.Texture,
                        new Rect2(0, 0, w, h),   // ← fix: (0,0) không phải (0, ScreenY)
                        entry.SourceRect,
                        modulate: new Color(1, 1, 1, call.Alpha));
                    DrawSetTransformMatrix(Transform2D.Identity);
                }
                else
                {
                    DrawTextureRectRegion(entry.Texture,
                        new Rect2(call.ScreenX, call.ScreenY, w, h),
                        entry.SourceRect,
                        modulate: new Color(1, 1, 1, call.Alpha));
                }
            }
        }
 
        // Placeholder — in production inject via constructor/DI
        private IPartRegistry GetPartRegistry() 
        {
            return Registry ?? throw new InvalidOperationException(
                "Registry chưa được inject vào CharacterRendererNode");
        }
 
        private static InMemoryPartRegistry BuildDefaultRegistry()
        {
            // Populated at startup from DB/JSON — see PartRegistryLoader
            return new InMemoryPartRegistry();
        }
    }
}