using Godot;
using System.Collections.Generic;
namespace Player
{
    // ─────────────────────────────────────────────────────────────────────────
    // 1. ATLAS LOADER — Godot implementation of sprite-ID → texture region
    //    Replaces SmallImage.imgbig[] + smallImg[][] lookup
    // ─────────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Maps integer sprite IDs to Godot AtlasTexture regions.
    /// Supports both individual PNGs and packed atlases.
    ///
    /// Legacy:  smallImg[id] = { atlasIndex, x, y, w, h }
    ///          imgbig[atlasIndex]
    ///          → g.drawRegion(imgbig[atlas], x, y, w, h, transform, screenX, screenY, anchor)
    ///
    /// Modern:  GetRegion(spriteId) → AtlasEntry { texture, srcRect }
    ///          → canvas.DrawTextureRectRegion(texture, destRect, srcRect)
    /// </summary>
    public sealed class SpriteAtlasLoader
    {
        public readonly struct AtlasEntry
        {
            public readonly Texture2D Texture;
            public readonly Rect2     SourceRect;
            public readonly bool      IsValid;
 
            public AtlasEntry(Texture2D tex, Rect2 src)
            {
                Texture    = tex;
                SourceRect = src;
                IsValid    = tex != null;
            }
        }
 
        // Individual PNG cache (fallback when no atlas)
        private readonly Dictionary<int, Texture2D> _individualCache = new();
 
        // Atlas: index → texture (replaces imgbig[])
        private readonly Dictionary<int, Texture2D> _atlasTextures = new();
 
        // Sprite map: spriteId → { atlasIndex, x, y, w, h } (replaces smallImg[][])
        private readonly Dictionary<int, int[]> _spriteMap = new();
 
        private readonly string _basePath;
 
        public SpriteAtlasLoader(string basePath = "res://asset/player/")
        {
            _basePath = basePath;
        }
 
        // ── Load individual PNG (legacy SmallImage fallback) ──────────────────
 
        public void LoadAtlas(int atlasIndex, string path)
        {
            if (ResourceLoader.Exists(path))
                _atlasTextures[atlasIndex] = GD.Load<Texture2D>(path);
        }
 
        public void RegisterSpriteRegion(int spriteId, int atlasIndex,
                                         int srcX, int srcY, int srcW, int srcH)
        {
            _spriteMap[spriteId] = new[] { atlasIndex, srcX, srcY, srcW, srcH };
        }
 
        public AtlasEntry GetSprite(int spriteId)
        {
            // 1. Try atlas
            if (_spriteMap.TryGetValue(spriteId, out var region)
                && _atlasTextures.TryGetValue(region[0], out var atlas))
            {
                return new AtlasEntry(atlas,
                    new Rect2(region[1], region[2], region[3], region[4]));
            }
 
            // 2. Fallback: individual PNG  (res://asset/character/SmallImage/Small{id}.png)
            if (!_individualCache.TryGetValue(spriteId, out var tex))
            {
                string path = $"{_basePath}{spriteId}.png";
                tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
                _individualCache[spriteId] = tex;
            }
 
            return tex != null
                ? new AtlasEntry(tex, new Rect2(0, 0, tex.GetWidth(), tex.GetHeight()))
                : default;
        }
 
        public void UnloadAll()
        {
            _atlasTextures.Clear();
            _individualCache.Clear();
            _spriteMap.Clear();
        }
    }
}