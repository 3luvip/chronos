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

    public string TileSpritesheetPath(int tileSetId)              => $"{BasePath}t/{tileSetId}.png";
    public string TileSubdirectoryFramePath(int tileSetId, int i) => $"{BasePath}t/{tileSetId}/t_{i:D2}.png";
    public string TileDirectFramePath(int frameId)                => $"{BasePath}t/{frameId}.png";
    public string BackgroundItemPath(int imageId)                 => $"{BasePath}mapBackGround/{imageId}.png";
    public string WaterfallTexturePath()                          => $"{BasePath}tWater/wtf.png";
    public string TopWaterfallTexturePath()                       => $"{BasePath}tWater/twtf.png";
    public string WaterflowTexturePath()                          => $"{BasePath}tWater/wts.png";
    public string WaterflowVariantNPath()                         => $"{BasePath}tWater/wtsN.png";
    public string WaterflowVariantN2Path()                        => $"{BasePath}tWater/wtsN2.png";
    public string ShadowTexturePath()                             => $"{BasePath}mainImage/shadowBig.png";
    public string LightOverlayTexturePath()                       => $"{BasePath}bg/light.png";

    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "res://";
        p = p.Trim();
        return p.EndsWith('/') ? p : p + '/';
    }
}
