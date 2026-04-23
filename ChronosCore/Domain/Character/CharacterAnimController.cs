using System.Collections.Generic;
using System;

namespace Chronos.Core.Domain.Character
{
    public sealed class CharacterAnimController
    {
        private readonly Dictionary<CharacterAnimState, AnimationClip> _clips = new();
 
        private CharacterAnimState _state = CharacterAnimState.Idle;
        private int   _frameIndex;
        private float _timer;
        private bool  _locked;   // once-clip in progress
 
        public CharacterAnimState CurrentState => _state;
        public int                CurrentFrame => _frameIndex;
 
        public event Action<CharacterAnimState>? ClipCompleted;
 
        public void RegisterClip(CharacterAnimState state, AnimationClip clip)
            => _clips[state] = clip;
 
        public bool RequestTransition(CharacterAnimState next, bool force = false)
        {
            if (!force && (_locked || _state == next)) return false;
            SwitchTo(next);
            return true;
        }
 
        /// <summary>Call once per game tick. dt in seconds.</summary>
        public void Tick(float dt)
        {
            if (!_clips.TryGetValue(_state, out var clip)) return;
 
            _timer += dt;
            float interval = 1f / clip.Fps;
 
            while (_timer >= interval)
            {
                _timer -= interval;
                _frameIndex++;
 
                if (_frameIndex >= clip.FrameCount)
                {
                    if (clip.Loop)
                    {
                        _frameIndex = 0;
                    }
                    else
                    {
                        _frameIndex = clip.FrameCount - 1;
                        var completed = _state;
                        _locked = false;
                        SwitchTo(CharacterAnimState.Idle);
                        ClipCompleted?.Invoke(completed);
                        return;
                    }
                }
            }
        }
 
        /// <summary>Get the current AnimationFrame for rendering. Returns null if no clip registered.</summary>
        public AnimationFrame? GetCurrentFrame()
        {
            if (!_clips.TryGetValue(_state, out var clip)) return null;
            if (clip.FrameCount == 0) return null;
            int safeIdx = Math.Clamp(_frameIndex, 0, clip.FrameCount - 1);
            return clip.Frames[safeIdx];
        }
 
        private void SwitchTo(CharacterAnimState next)
        {
            _state      = next;
            _frameIndex = 0;
            _timer      = 0f;
            _locked     = _clips.TryGetValue(next, out var c) && !c.Loop;
        }
    }
}