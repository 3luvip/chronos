using System;

namespace Chronos.Core.Domain.Animation;

public enum AnimationState { Idle, Run, Attack, Jump, Die }

/// <summary>
/// Pure state machine. Không có Godot, không render.
/// Consumer đọc CurrentState + CurrentFrame để vẽ.
/// </summary>
public sealed class AnimationStateMachine
{
    private static readonly float[] Fps        = { 8f, 10f, 12f, 10f, 6f };
    private static readonly int[]   FrameCount = {  4,   8,   6,   5,  8  };
    private static readonly bool[]  IsOnce     = { false, false, true, false, true };

    private AnimationState _current = AnimationState.Idle;
    private int   _frameIdx;
    private float _frameTimer;
    private bool  _locked;

    public AnimationState CurrentState => _current;
    public int            CurrentFrame => _frameIdx;
    public string         CurrentName  => _current.ToString().ToLowerInvariant();

    /// Fired khi once-animation kết thúc (không dùng Godot signal).
    public event Action<AnimationState>? AnimationCompleted;

    public void Tick(float deltaSeconds)
    {
        _frameTimer += deltaSeconds;
        float interval = 1f / Fps[(int)_current];
        if (_frameTimer < interval) return;

        _frameTimer -= interval;
        _frameIdx    = (_frameIdx + 1) % FrameCount[(int)_current];

        if (_frameIdx == 0 && IsOnce[(int)_current])
        {
            var completed = _current;
            _locked   = false;
            _current  = AnimationState.Idle;
            _frameIdx = 0;
            AnimationCompleted?.Invoke(completed);
        }
    }

    /// Trả về false nếu bị lock (once-animation đang chạy).
    public bool RequestTransition(AnimationState next)
    {
        if (_locked || _current == next) return false;
        _current    = next;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = IsOnce[(int)next];
        return true;
    }

    /// Server authority — bypass lock.
    public void ForceTransition(AnimationState next)
    {
        _locked     = false;
        _current    = next;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = IsOnce[(int)next];
    }

    public void Reset()
    {
        _current    = AnimationState.Idle;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = false;
    }
}
