namespace Map
{
    /// <summary>
    /// Centralized animation tick counter shared across all rendering layers.
    /// Acts as the single source of truth for frame animation state,
    /// preventing drift between independently ticking layers.
    /// </summary>
    public sealed class MapAnimClock
    {
        private const int MAX_TICK         = 10_000;
        private const int WATERFALL_PERIOD = 8;
        private const int WATERFALL_FRAMES = 4;
        private const int WATERFLOW_FRAMES = 2;

        private int _currentTick;

        /// <summary>Current raw tick counter. Wraps at <see cref="MAX_TICK"/>.</summary>
        public int CurrentTick => _currentTick;

        /// <summary>Waterfall animation frame index (0–3), cycling every 2 ticks.</summary>
        public int WaterfallFrame => (_currentTick % WATERFALL_PERIOD) / (WATERFALL_PERIOD / WATERFALL_FRAMES);

        /// <summary>Waterflow animation frame index (0–1), cycling every 4 ticks.</summary>
        public int WaterflowFrame => (_currentTick % WATERFALL_PERIOD) / (WATERFALL_PERIOD / WATERFLOW_FRAMES);

        /// <summary>Advances the tick counter by one frame.</summary>
        public void Advance() => _currentTick = (_currentTick + 1) % MAX_TICK;

        /// <summary>Resets the tick counter to zero. Call when loading a new map.</summary>
        public void Reset() => _currentTick = 0;
    }
}