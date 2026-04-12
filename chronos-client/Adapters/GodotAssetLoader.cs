using System.Collections.Generic;
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

/// <summary>
/// Maps TextureHandle (int ID) ↔ Godot Texture2D.
/// Core chỉ biết TextureHandle — không bao giờ chạm Texture2D.
/// </summary>
public sealed class GodotAssetLoader : IAssetLoader
{
    private readonly Dictionary<int, Texture2D> _bank  = new();
    private readonly Dictionary<string, int>    _paths = new();
    private int _nextId = 1;

    public TextureHandle LoadTexture(string path)
    {
        if (_paths.TryGetValue(path, out int existing))
            return new TextureHandle(existing);

        if (!ResourceLoader.Exists(path)) return TextureHandle.None;

        var tex = GD.Load<Texture2D>(path);
        if (tex is null) return TextureHandle.None;

        int id = _nextId++;
        _bank[id]    = tex;
        _paths[path] = id;
        return new TextureHandle(id);
    }

    public void ReleaseTexture(TextureHandle handle)
    {
        if (!handle.IsValid) return;
        _bank.Remove(handle.Id);
    }

    public (int width, int height) GetTextureDimensions(TextureHandle handle)
    {
        if (!handle.IsValid || !_bank.TryGetValue(handle.Id, out var tex))
            return (0, 0);
        return (tex.GetWidth(), tex.GetHeight());
    }

    /// Godot-only: resolve handle → Texture2D để vẽ.
    public Texture2D? Resolve(TextureHandle handle) =>
        handle.IsValid && _bank.TryGetValue(handle.Id, out var tex) ? tex : null;
}
