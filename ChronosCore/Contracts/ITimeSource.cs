namespace Chronos.Core.Contracts;

public interface ITimeSource
{
    /// <summary>Monotonic milliseconds. Dùng cho game timing.</summary>
    long TickMs { get; }
    /// <summary>Wall-clock UTC ms. Dùng cho session timestamps.</summary>
    long UtcMs  { get; }
}
