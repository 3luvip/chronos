using Godot;
using System;

public partial class AnimationController : Node
{
    public enum State { Idle, Run, Attack, Jump, Die }

    private State            _current = State.Idle;
    private CharacterRenderer _renderer;
    private float            _frameTimer;
    private int              _frameIdx;

    // FPS per state
    private static readonly float[] Fps = { 8, 10, 12, 10, 6 };  // Idle,Run,Attack,Jump,Die
    private static readonly int[]   FrameCount = { 4, 8, 6, 5, 8 };
    private static readonly string[] Names = { "idle","run","attack","jump","die" };

    public void Transition(State next)
    {
        if (_current == next) return;
        // Once-animations khoá transition cho đến khi xong
        bool currentIsOnce = _current is State.Attack or State.Die;
        if (currentIsOnce) return;

        _current   = next;
        _frameIdx  = 0;
        _frameTimer = 0;
        _renderer.ApplyFrame(Names[(int)next], 0);
    }

    public override void _Process(double delta)
    {
        _frameTimer += (float)delta;
        float interval = 1f / Fps[(int)_current];

        if (_frameTimer >= interval)
        {
            _frameTimer -= interval;
            _frameIdx   = (_frameIdx + 1) % FrameCount[(int)_current];

            bool isDone = _frameIdx == 0 &&
                          _current is State.Attack or State.Jump or State.Die;
            if (isDone)
                Transition(State.Idle);
            else
                _renderer.ApplyFrame(Names[(int)_current], _frameIdx);
        }
    }
}