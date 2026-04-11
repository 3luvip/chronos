using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Spatial acceleration structure for background decoration items.
    /// Decouples frustum-culling logic from the rendering layer, and allows
    /// the production uniform-grid implementation to be swapped for a simple
    /// linear scan during tests.
    /// </summary>
    public interface IBackgroundItemSpatialIndex
    {
        /// <summary>
        /// Rebuilds the index from the given item collection.
        /// Call after items are added or the map changes.
        /// </summary>
        void Build(IReadOnlyList<BackgroundItem> items);

        /// <summary>
        /// Fills <paramref name="results"/> with all items that may be visible
        /// in the axis-aligned region defined by the camera and viewport.
        /// The list is cleared before writing — no allocation is performed.
        /// </summary>
        void Query(int cameraX, int cameraY, int viewportWidth, int viewportHeight,
                   List<BackgroundItem> results);

        /// <summary>Removes all items from the index. Call on map unload.</summary>
        void Clear();
    }
}