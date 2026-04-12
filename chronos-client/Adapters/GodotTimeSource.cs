using System;
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotTimeSource : ITimeSource
{
    public long TickMs => (long)Time.GetTicksMsec();
    public long UtcMs  => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
