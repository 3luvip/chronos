using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character {
    /// <summary>
    /// One resolved draw instruction.
    /// Replaces SmallImage.drawSmallImage(g, id, x, y, transform, anchor).
    /// </summary>
    public readonly struct SpriteDrawCall
    {
        public readonly int  SpriteId;   // atlas entry id
        public readonly int  ScreenX;    // final screen x (anchor already resolved)
        public readonly int  ScreenY;    // final screen y
        public readonly bool FlipH;      // replaces transform=2 (TRANS_MIRROR)
        public readonly int  ZOrder;     // replaces call-order layering
        public readonly float Alpha;     // for death fade / blink effects
 
        public SpriteDrawCall(int spriteId, int screenX, int screenY,
                              bool flipH = false, int zOrder = 30, float alpha = 1f)
        {
            SpriteId = spriteId;
            ScreenX  = screenX;
            ScreenY  = screenY;
            FlipH    = flipH;
            ZOrder   = zOrder;
            Alpha    = alpha;
        }
    }
}