using System;

namespace Chronos.Core.Domain.Map;

/// <summary>Centralised registry of every asset path — pure C#, no Godot.</summary>
public sealed class MapAssetPaths
{
    public string BasePath { get; }

    public MapAssetPaths(string basePath = "res://")
    {
        BasePath = NormalizePath(basePath);
    }

    // ── Tile assets ───────────────────────────────────────────────────────────
    public string TileSpritesheetPath(int tileSetId)              => $"{BasePath}asset/tileset/{tileSetId}.png";
    public string TileSubdirectoryFramePath(int tileSetId, int i) => $"{BasePath}asset/tileset/{tileSetId}/t_{i:D2}.png";
    public string TileDirectFramePath(int frameId)                => $"{BasePath}asset/tileset/{frameId}.png";

    // ── Background decoration items ───────────────────────────────────────────
    public string BackgroundItemPath(int imageId)                 => $"{BasePath}asset/mapBackground/{imageId}.png";

    // ── Water textures ────────────────────────────────────────────────────────
    public string WaterfallTexturePath()                          => $"{BasePath}asset/tWater/wtf.png";
    public string TopWaterfallTexturePath()                       => $"{BasePath}asset/tWater/twtf.png";
    public string WaterflowTexturePath()                          => $"{BasePath}asset/tWater/wts.png";
    public string WaterflowVariantNPath()                         => $"{BasePath}asset/tWater/wtsN.png";
    public string WaterflowVariantN2Path()                        => $"{BasePath}asset/tWater/wtsN2.png";

    // ── Misc ──────────────────────────────────────────────────────────────────
    public string ShadowTexturePath()                             => $"{BasePath}asset/mainImage/shadowBig.png";
    public string LightOverlayTexturePath()                       => $"{BasePath}asset/bg/light.png";

    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "res://";
        p = p.Trim();
        return p.EndsWith('/') ? p : p + '/';
    }
}