using Godot;
using System.Collections.Generic;

public partial class CharacterRenderer : Node2D
{
    // Cache toàn cục: sprite_id → Texture2D (tránh load lại)
    private static readonly Dictionary<int, Texture2D> _texCache = new();

    private readonly List<(CharacterPart part, Sprite2D sprite)> _layers = new();

    public string  CurrentAnim  { get; private set; } = "idle";
    public int     CurrentFrame { get; private set; } = 0;
    public bool    FacingRight  { get; set; } = true;

    // Gọi khi thay / trang bị mảnh mới
    public void SetPart(CharacterPart part)
    {
        // Xóa sprite cũ cùng layer nếu có
        var existing = _layers.FindIndex(l => l.part.PartType == part.PartType);
        if (existing >= 0)
        {
            _layers[existing].sprite.QueueFree();
            _layers.RemoveAt(existing);
        }

        var sprite = new Sprite2D
        {
            ZIndex  = part.Layer,
            Texture = LoadTexture(part.SpriteId),
        };
        AddChild(sprite);
        _layers.Add((part, sprite));

        // Sắp xếp lại theo layer để render đúng thứ tự
        _layers.Sort((a, b) => a.part.Layer.CompareTo(b.part.Layer));

        ApplyFrame(CurrentAnim, CurrentFrame);
    }

    // Gọi mỗi tick animation
    public void ApplyFrame(string anim, int frame)
    {
        CurrentAnim  = anim;
        CurrentFrame = frame;

        foreach (var (part, sprite) in _layers)
        {
            var (dx, dy) = part.GetOffset(anim, frame);
            sprite.Position = new Vector2(dx * (FacingRight ? 1 : -1), dy);
            sprite.FlipH    = !FacingRight ^ part.FlipH;
            sprite.Texture  = LoadTexture(part.SpriteId);
        }
    }

    private static Texture2D LoadTexture(int id)
    {
        if (!_texCache.TryGetValue(id, out var tex))
        {
            tex = GD.Load<Texture2D>($"res://sprites/parts/{id}.png");
            _texCache[id] = tex;
        }
        return tex;
    }
}