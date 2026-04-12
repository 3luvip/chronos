namespace Chronos.Core.Contracts;

public interface IDrawContext
{
    void DrawTexture(TextureHandle handle, int screenX, int screenY);
    void DrawTextureRegion(
        TextureHandle handle,
        int screenX,  int screenY,
        int srcX,     int srcY,
        int srcWidth, int srcHeight,
        int dstWidth, int dstHeight);
    void DrawTextureFlippedH(TextureHandle handle, int screenX, int screenY, int width);
    void SetTransform(float scaleX, float scaleY, float tx, float ty);
    void ResetTransform();
}
