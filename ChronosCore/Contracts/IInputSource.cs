namespace Chronos.Core.Contracts;

public interface IInputSource
{
    InputSnapshot Capture(long nowMs);
}
