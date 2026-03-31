// Data record cho mỗi mảnh ghép nhân vật
using System.Collections.Generic;
public record PartFrame(int Dx, int Dy);   // offset per animation frame

public sealed class CharacterPart
{
    public string   PartType  { get; init; }  // "head", "body", "legs", "weapon", "aura"
    public int      SpriteId  { get; set; }
    public int      Layer     { get; init; }  // thứ tự render: 0=legs, 1=body, 2=weapon, 3=head, 4=aura
    public bool     FlipH     { get; set; }

    // Offset per anim per frame:  offsets[animName][frameIndex]
    public Dictionary<string, PartFrame[]> Offsets { get; init; } = new();

    public PartFrame GetOffset(string anim, int frame)
    {
        if (Offsets.TryGetValue(anim, out var frames) && frame < frames.Length)
            return frames[frame];
        return new PartFrame(0, 0);
    }
}