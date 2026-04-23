using System;
using System.Collections.Generic;
namespace Chronos.Core.Domain.Character
{
    public sealed class AnimationFrame
    {
        public const int PART_HEAD   = 0;
        public const int PART_LEG    = 1;
        public const int PART_BODY   = 2;
        public const int PART_BAG    = 3;
        public const int PART_COUNT  = 4;

        public readonly PartOffset[] Parts;

        public AnimationFrame(PartOffset[] parts)
        {
            if (parts.Length != PART_COUNT)
                throw new ArgumentException($"Expected {PART_COUNT} parts, got {parts.Length}");
            Parts = parts;
        }

        // Convert one row from CharInfo[cf]
        public static AnimationFrame FromLegacy(int[][] legacyRow)
        {
            var parts = new PartOffset[PART_COUNT];
            for (int i = 0; i < PART_COUNT; i++)
                parts[i] = legacyRow[i].Length >= 3
                    ? PartOffset.FromLegacy(legacyRow[i])
                    : default;
            return new AnimationFrame(parts);
        }
        
    }
}