namespace Chronos.Core.Contracts;

public interface IAssetLoader
{
    TextureHandle LoadTexture(string path);
    void          ReleaseTexture(TextureHandle handle);
    (int width, int height) GetTextureDimensions(TextureHandle handle);
}
