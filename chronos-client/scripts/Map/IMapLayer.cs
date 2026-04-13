using Chronos.Core.Domain.Map;
using Godot;

namespace Map
{
    /// <summary>
    /// Contract for every visual layer in the render pipeline.
    /// Implement this interface to add new layers without modifying existing code
    /// (Open/Closed Principle). Layers are registered with
    /// <see cref="MapRenderPipeline.RegisterLayer"/> and sorted by <see cref="ZOrder"/>.
    /// </summary>
    public interface IMapLayer
    {
        /// <summary>
        /// Draw order. Lower values are drawn first (further back).
        /// Suggested values: 10=far parallax, 20=bg, 30=tiles, 35=water,
        /// 40=overlay, 60=foreground, 70=lighting.
        /// </summary>
        int ZOrder { get; }

        /// <summary>
        /// Called when a new map is loaded. Use to initialise or pre-compute
        /// any map-dependent state (e.g. water surface Y, spatial indices).
        /// </summary>
        void OnMapLoaded(TileMapData map, MapCamera camera);

        /// <summary>
        /// Called when the current map is unloaded. Release any map-scoped
        /// resources or clear accumulated state.
        /// </summary>
        void OnMapUnloaded();

        /// <summary>
        /// Called once per frame in <c>_Process</c>. Update animation state here.
        /// Must NOT issue draw calls — only mutate this layer's own state.
        /// </summary>
        /// <param name="animationTick">Current animation tick from <see cref="MapAnimClock"/>.</param>
        void Tick(int animationTick);

        /// <summary>
        /// Called once per frame in <c>_Draw</c>. Issue draw calls here.
        /// Must NOT mutate state — treat all fields as read-only during Draw.
        /// </summary>
        void Draw(CanvasItem canvas, MapCamera camera);
    }
}