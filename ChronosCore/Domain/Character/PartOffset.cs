using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public readonly struct PartOffset
    {
        public readonly int ImageIndex;
        public readonly int Dx;
        public readonly int Dy;
        public PartOffset(int imageIndex, int dx, int dy)
        {
            ImageIndex = imageIndex;
            Dx = dx;
            Dy = dy;
        }
        
        public static PartOffset FromLegacy(int[] legacy)
            => new(legacy[0], legacy[1], legacy.Length > 2 ? legacy[2] : 0);
    }
}