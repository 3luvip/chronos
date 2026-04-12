using System;

namespace Chronos.Core.Contracts;

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
