using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

/// <summary>
/// Bridges IDrawContext → Godot CanvasItem draw calls.
/// Chỉ được khởi tạo trong _Draw() — không lưu trữ lâu dài.
/// </summary>
public sealed class GodotDrawContext : IDrawContext
{
    private readonly CanvasItem      _canvas;
    private readonly GodotAssetLoader _loader;

    public GodotDrawContext(CanvasItem canvas, GodotAssetLoader loader)
    {
        _canvas = canvas;
        _loader = loader;
    }

    public void DrawTexture(TextureHandle h, int x, int y)
    {
        var tex = _loader.Resolve(h);
        if (tex is null) return;
        _canvas.DrawTexture(tex, new Vector2(x, y));
    }

    public void DrawTextureRegion(TextureHandle h,
        int sx, int sy, int srcX, int srcY, int srcW, int srcH, int dstW, int dstH)
    {
        var tex = _loader.Resolve(h);
        if (tex is null) return;
        var src = new Rect2(srcX, srcY, srcW, srcH);
        var dst = new Rect2(sx,   sy,   dstW, dstH);
        _canvas.DrawTextureRectRegion(tex, dst, src);
    }

    public void DrawTextureFlippedH(TextureHandle h, int x, int y, int w)
    {
        var tex = _loader.Resolve(h);
        if (tex is null) return;
        _canvas.DrawSetTransformMatrix(new Transform2D(-1, 0, 0, 1, x + w, y));
        _canvas.DrawTexture(tex, Vector2.Zero);
        _canvas.DrawSetTransformMatrix(Transform2D.Identity);
    }

    public void SetTransform(float sx, float sy, float tx, float ty) =>
        _canvas.DrawSetTransformMatrix(new Transform2D(sx, 0, 0, sy, tx, ty));

    public void ResetTransform() =>
        _canvas.DrawSetTransformMatrix(Transform2D.Identity);
}
