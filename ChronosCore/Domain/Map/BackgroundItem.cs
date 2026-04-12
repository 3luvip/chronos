using System.Collections.Generic;
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Map;

public sealed class BackgroundItem
{
    public int Id;
    public int ImageId;
    public int WorldX, WorldY, OffsetX, OffsetY;
    public int Transform;
    public int Layer;

    private static readonly HashSet<int> MiniBgIds =
        [79, 80, 81, 85, 86, 90, 91, 92, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108];

    private static readonly HashSet<int> NoBlendIds =
        [79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 95, 144,
         99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112,
         113, 114, 115, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127,
         132, 133, 134, 139, 140, 141, 142, 143, 145, 146, 147, 171, 229, 218];

    private static readonly HashSet<int> MirrorExcludedIds = [156, 157, 159, 165, 167, 168, 169, 170, 238];

    public bool IsMiniBgItem => MiniBgIds.Contains(ImageId);
    public bool IsNoBlend    => NoBlendIds.Contains(ImageId);
    public bool ShouldMirrorInDoubleMap =>
        ImageId > 137 &&
        !MirrorExcludedIds.Contains(ImageId) &&
        !(ImageId >= 241 && ImageId < 266);

    public Vec2I GetParallaxWorldPos(int camX, int camY)
    {
        int px = 0, py = 0;
        if (Layer == 4) { px = -camX / 2 + 100; }
        else if (IsSpecialLayer3() && Layer == 3) { px = -camX / 3 + 200; }
        if (IsMiniBgItem && Layer < 4) { px = -(camX >> 4) + 50; py = (camY >> 5) - 15; }
        return new Vec2I(WorldX + OffsetX + px, WorldY + OffsetY + py);
    }

    public bool IsVisibleInViewport(int camX, int camY, int vw, int vh, int imgW, int imgH)
    {
        var pos = GetParallaxWorldPos(camX, camY);
        return pos.X + imgW >= camX && pos.X <= camX + vw &&
               pos.Y + imgH >= camY && pos.Y <= camY + vh;
    }

    private static readonly HashSet<int> Layer3ParallaxIds = [28, 67, 68, 69, 70];
    private bool IsSpecialLayer3() => Layer3ParallaxIds.Contains(ImageId);
}
