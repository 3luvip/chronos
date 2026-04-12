using System;
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

/// <summary>Bridges ILogger → GD.Print. Godot chỉ tồn tại ở đây.</summary>
public sealed class GodotLogger : ILogger
{
    private readonly string _prefix;
    public GodotLogger(string prefix = "") => _prefix = prefix.Length > 0 ? $"[{prefix}] " : "";

    public void Info (string msg)              => GD.Print($"{_prefix}{msg}");
    public void Warn (string msg)              => GD.PushWarning($"{_prefix}{msg}");
    public void Error(string msg, Exception? ex = null)
    {
        GD.PrintErr($"{_prefix}{msg}");
        if (ex is not null) GD.PrintErr(ex.ToString());
    }
    public void Debug(string msg)              => GD.Print($"{_prefix}[DBG] {msg}");
}
