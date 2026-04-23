using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public sealed class AnimationClipRegistry
    {
        private readonly Dictionary<string, AnimationClip> _clips = new();
 
        public void Register(AnimationClip clip) => _clips[clip.Name] = clip;
 
        public AnimationClip? Get(string name)
            => _clips.TryGetValue(name, out var c) ? c : null;
 
        // ── Legacy CharInfo → clips conversion ─────────────────────────────────
        // CharInfo has 33 rows. The statusMe/cf mapping from legacy:
        //   cf 0-7   → stand/idle cycle (8 frames)
        //   cf 8-11  → run (4 frames)
        //   cf 12    → jump up
        //   cf 13-15 → fall
        //   cf 16-22 → attack variants
        //   cf 23    → hurt
        //   cf 24-25 → special states
        //   etc.
        public static AnimationClipRegistry FromLegacyCharInfo(int[][][] charInfo)
        {
            var reg = new AnimationClipRegistry();
 
            reg.Register(AnimationClip.FromLegacyRange("idle",   8f, true,  charInfo, 0,  8));
            reg.Register(AnimationClip.FromLegacyRange("run",    10f, true,  charInfo, 8,  4));
            reg.Register(AnimationClip.FromLegacyRange("jump",   8f, false, charInfo, 12, 1));
            reg.Register(AnimationClip.FromLegacyRange("fall",   8f, true,  charInfo, 13, 3));
            reg.Register(AnimationClip.FromLegacyRange("attack", 12f, false, charInfo, 16, 7));
            reg.Register(AnimationClip.FromLegacyRange("hurt",   8f, false, charInfo, 23, 1));
            reg.Register(AnimationClip.FromLegacyRange("sit",    6f, true,  charInfo, 24, 2));
            // skill, fly, charge frames can be mapped similarly
 
            return reg;
        }
    }
}