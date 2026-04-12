namespace Chronos.Core.Domain.Map;

/// <summary>
/// Centralized animation tick counter — deterministic, no engine types.
/// </summary>
public sealed class MapAnimClock
{
    private const int MaxTick         = 10_000;
    private const int WaterfallPeriod = 8;
    private const int WaterfallFrames = 4;
    private const int WaterflowFrames = 2;

    private int _tick;

    public int CurrentTick    => _tick;
    public int WaterfallFrame => (_tick % WaterfallPeriod) / (WaterfallPeriod / WaterfallFrames);
    public int WaterflowFrame => (_tick % WaterfallPeriod) / (WaterfallPeriod / WaterflowFrames);

    public void Advance() => _tick = (_tick + 1) % MaxTick;
    public void Reset()   => _tick = 0;

    public int  Serialize()           => _tick;
    public void Deserialize(int tick) => _tick = tick % MaxTick;
}
