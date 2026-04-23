using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    /// <summary>
    /// Collects SpriteDrawCalls from all characters in the scene,
    /// sorts by ZOrder, and presents a flat ordered list for the renderer.
    /// Replaces function-call ordering in paintCharBody().
    /// </summary>
    public sealed class RenderLayerStack
    {
        private readonly List<SpriteDrawCall> _calls = new(512);
 
        public void Clear() => _calls.Clear();
 
        public void Add(SpriteDrawCall call) => _calls.Add(call);
 
        public void AddRange(IEnumerable<SpriteDrawCall> calls)
        {
            foreach (var c in calls) _calls.Add(c);
        }
 
        /// <summary>Sort by ZOrder ascending — call once per frame before drawing.</summary>
        public void Sort()
            => _calls.Sort(static (a, b) => a.ZOrder.CompareTo(b.ZOrder));
 
        public IReadOnlyList<SpriteDrawCall> Sorted => _calls;
    }
}