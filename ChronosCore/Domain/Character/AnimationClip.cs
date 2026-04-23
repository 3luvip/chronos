using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    /// <summary>
    /// Data-driven animation clip — replaces hardcoded CharInfo and statusMe switch.
    /// Contains ALL frames for one animation state (idle, run, jump, …).
    /// </summary>
    public sealed class AnimationClip
    {
        public string Name         { get; }
        public float  Fps          { get; }
        public bool   Loop         { get; }
        public AnimationFrame[] Frames { get; }
 
        public int FrameCount => Frames.Length;
 
        public AnimationClip(string name, float fps, bool loop, AnimationFrame[] frames)
        {
            Name   = name;
            Fps    = fps;
            Loop   = loop;
            Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }
 
        /// <summary>
        /// Convert legacy CharInfo (33 rows × 4 parts × 3 fields) into a flat sequence
        /// of clips by providing frame-range mapping.
        /// </summary>
        public static AnimationClip FromLegacyRange(
            string name, float fps, bool loop,
            int[][][] charInfo, int startFrame, int frameCount)
        {
            var frames = new AnimationFrame[frameCount];
            for (int i = 0; i < frameCount; i++)
                frames[i] = AnimationFrame.FromLegacy(charInfo[startFrame + i]);
            return new AnimationClip(name, fps, loop, frames);
        }
    }
}