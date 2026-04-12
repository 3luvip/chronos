namespace Chronos.Core.Contracts;

/// <summary>
/// Opaque handle tới một texture đã load.
/// Core layer chỉ biết ID integer — không bao giờ chạm vào Godot Texture2D.
/// </summary>
public readonly record struct TextureHandle(int Id)
{
    public static readonly TextureHandle None = new(0);
    public bool IsValid => Id != 0;
}
