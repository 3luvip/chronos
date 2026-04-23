using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public readonly struct PartImageEntry
    {
        /// <summary>Image/sprite ID (maps to atlas region or individual PNG).</summary>
        public readonly int  SpriteId;
        public readonly int  Dx;
        public readonly int  Dy;
 
        public PartImageEntry(int spriteId, int dx, int dy)
        {
            SpriteId = spriteId;
            Dx       = dx;
            Dy       = dy;
        }
    }
}