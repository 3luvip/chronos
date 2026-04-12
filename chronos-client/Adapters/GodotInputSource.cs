using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotInputSource : IInputSource
{
    public InputSnapshot Capture(long nowMs)
    {
        var dir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        return new InputSnapshot
        {
            MoveX        = dir.X,
            MoveY        = dir.Y,
            Attack       = Input.IsActionJustPressed("attack"),
            Jump         = Input.IsActionJustPressed("jump"),
            CapturedAtMs = nowMs,
        };
    }
}
