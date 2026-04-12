using System.Collections.Generic;

namespace Chronos.Core.Domain.Character;

public readonly record struct PartFrame(int Dx, int Dy);

public sealed class CharacterPart
{
    public const int LayerLegs   = 0;
    public const int LayerBody   = 1;
    public const int LayerWeapon = 2;
    public const int LayerHead   = 3;
    public const int LayerAura   = 4;

    public required string                               PartType { get; init; }
    public required int                                  SpriteId { get; init; }
    public required int                                  Layer    { get; init; }
    public          bool                                 FlipH    { get; init; }
    public required IReadOnlyDictionary<string, PartFrame[]> Offsets { get; init; }

    public PartFrame GetOffset(string anim, int frame)
    {
        if (Offsets.TryGetValue(anim, out var frames) && (uint)frame < (uint)frames.Length)
            return frames[frame];
        return new PartFrame(0, 0);
    }
}
