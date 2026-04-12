using System;
using System.Collections.Generic;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class SpyLogger : ILogger
{
    public List<string> InfoMessages  { get; } = new();
    public List<string> WarnMessages  { get; } = new();
    public List<string> ErrorMessages { get; } = new();
    public List<string> DebugMessages { get; } = new();

    public void Info (string msg)              => InfoMessages.Add(msg);
    public void Warn (string msg)              => WarnMessages.Add(msg);
    public void Error(string msg, Exception? ex = null) => ErrorMessages.Add(msg);
    public void Debug(string msg)              => DebugMessages.Add(msg);
}
