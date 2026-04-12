using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class FakeTimeSource : ITimeSource
{
    public long TickMs { get; set; }
    public long UtcMs  { get; set; }

    public void Advance(long ms) { TickMs += ms; UtcMs += ms; }
}
