using Godot;
using System.Collections.Generic;

namespace Map
{
    /// <summary>
    /// Data for a single map decoration object (tree, rock, building, etc.).
    /// Pure data class — no render calls.
    ///
    /// Layer conventions:
    ///   1 = background (behind tiles)
    ///   2 = special overlay
    ///   3 = foreground (in front of entities)
    ///   4 = far parallax (slowest scroll)
    /// </summary>
    public sealed class BackgroundItem
    {
        public int Id;
        public int ImageId;
        public int WorldX;
        public int WorldY;
        public int OffsetX;
        public int OffsetY;

        /// <summary>Horizontal transform: 0 = normal, 2 = flip horizontally.</summary>
        public int Transform;

        public int Layer;

        // ── Classification lookup sets ────────────────────────────────────────────

        private static readonly HashSet<int> MiniBgImageIds = new HashSet<int>
        {
            79, 80, 81, 85, 86, 90, 91, 92,
            99, 100, 101, 102, 103, 104, 105, 106, 107, 108
        };

        private static readonly HashSet<int> NoBlendImageIds = new HashSet<int>
        {
            79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 95, 144,
            99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112,
            113, 114, 115, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127,
            132, 133, 134, 139, 140, 141, 142, 143, 145, 146, 147, 171, 229, 218
        };

        private static readonly HashSet<int> MirrorExcludedImageIds = new HashSet<int>
        {
            156, 157, 159, 165, 167, 168, 169, 170, 238
        };

        // Range of image IDs that should never be mirrored in double maps
        private const int MIRROR_EXCLUDE_RANGE_START = 241;
        private const int MIRROR_EXCLUDE_RANGE_END   = 266;

        /// <summary>True when this item renders at a smaller scale (mini decoration).</summary>
        public bool IsMiniBgItem => MiniBgImageIds.Contains(ImageId);

        /// <summary>True when this item should skip additive/blend rendering.</summary>
        public bool IsNoBlend    => NoBlendImageIds.Contains(ImageId);

        /// <summary>
        /// Whether this item should be mirrored across the centre axis in double maps.
        /// ImageIds &gt; 137 are eligible, except those in the excluded set or the excluded range.
        /// </summary>
        public bool ShouldMirrorInDoubleMap
            => ImageId > 137
               && !MirrorExcludedImageIds.Contains(ImageId)
               && !(ImageId >= MIRROR_EXCLUDE_RANGE_START && ImageId < MIRROR_EXCLUDE_RANGE_END);

        // ── World position ────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the parallax-adjusted world position relative to the camera.
        /// Layer 4 scrolls at half speed; mini items use a separate reduced speed.
        /// Certain special image IDs on layer 3 also use reduced parallax.
        /// </summary>
        public Vector2I GetParallaxWorldPos(int cameraX, int cameraY)
        {
            int parallaxOffsetX = 0;
            int parallaxOffsetY = 0;

            if (Layer == 4)
            {
                parallaxOffsetX = -cameraX / 2 + 100;
            }
            else if (IsSpecialLayer3ParallaxImage() && Layer == 3)
            {
                parallaxOffsetX = -cameraX / 3 + 200;
            }

            if (IsMiniBgItem && Layer < 4)
            {
                parallaxOffsetX = -(cameraX >> 4) + 50;
                parallaxOffsetY =  (cameraY >> 5) - 15;
            }

            return new Vector2I(WorldX + OffsetX + parallaxOffsetX,
                                WorldY + OffsetY + parallaxOffsetY);
        }

        /// <summary>
        /// Returns true if this item's bounding box overlaps the visible viewport.
        /// </summary>
        public bool IsVisibleInViewport(int cameraX, int cameraY,
                                        int viewportWidth, int viewportHeight,
                                        int imageWidth, int imageHeight)
        {
            var pos = GetParallaxWorldPos(cameraX, cameraY);
            return pos.X + imageWidth  >= cameraX && pos.X <= cameraX + viewportWidth  &&
                   pos.Y + imageHeight >= cameraY && pos.Y <= cameraY + viewportHeight;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static readonly HashSet<int> Layer3ParallaxImageIds = new HashSet<int>
        {
            28, 67, 68, 69, 70
        };

        private bool IsSpecialLayer3ParallaxImage()
            => Layer3ParallaxImageIds.Contains(ImageId);
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uniform-grid spatial index for background items.
    /// Queries run in O(cells within viewport) instead of O(total items),
    /// making this suitable for maps with hundreds of decoration objects.
    ///
    /// Implements <see cref="IBackgroundItemSpatialIndex"/>.
    /// </summary>
    public sealed class BackgroundItemGrid : IBackgroundItemSpatialIndex
    {
        private const int CELL_SIZE_PIXELS = 256;

        private readonly Dictionary<ulong, List<BackgroundItem>> _cells = new();

        /// <inheritdoc/>
        public void Build(IReadOnlyList<BackgroundItem> items)
        {
            _cells.Clear();
            foreach (var item in items)
            {
                ulong key = EncodeCellKey(item.WorldX / CELL_SIZE_PIXELS,
                                          item.WorldY / CELL_SIZE_PIXELS);
                if (!_cells.TryGetValue(key, out var cell))
                    _cells[key] = cell = new List<BackgroundItem>();
                cell.Add(item);
            }
        }

        /// <inheritdoc/>
        public void Query(int cameraX, int cameraY, int viewportWidth, int viewportHeight,
                          List<BackgroundItem> results)
        {
            results.Clear();

            int cellX0 = cameraX / CELL_SIZE_PIXELS - 1;
            int cellX1 = (cameraX + viewportWidth)  / CELL_SIZE_PIXELS + 1;
            int cellY0 = cameraY / CELL_SIZE_PIXELS - 1;
            int cellY1 = (cameraY + viewportHeight) / CELL_SIZE_PIXELS + 1;

            for (int cx = cellX0; cx <= cellX1; cx++)
            for (int cy = cellY0; cy <= cellY1; cy++)
            {
                ulong key = EncodeCellKey(cx, cy);
                if (_cells.TryGetValue(key, out var cell))
                    results.AddRange(cell);
            }
        }

        /// <inheritdoc/>
        public void Clear() => _cells.Clear();

        /// <summary>
        /// Packs (cellX, cellY) into a single ulong key.
        /// Avoids ValueTuple boxing that would otherwise produce GC pressure.
        /// </summary>
        private static ulong EncodeCellKey(int cellX, int cellY)
            => ((ulong)(uint)cellX << 32) | (uint)cellY;
    }
}