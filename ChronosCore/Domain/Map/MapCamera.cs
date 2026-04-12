using System;
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Map;

/// <summary>Pure camera — không có Godot. Sub-pixel fixed-point scrolling.</summary>
public sealed class MapCamera
{
    public int PositionX { get; private set; }
    public int PositionY { get; private set; }
    public int TargetX   { get; private set; }
    public int TargetY   { get; private set; }
    public int MaxScrollX { get; private set; }
    public int MaxScrollY { get; private set; }
    public int ViewportWidth  { get; private set; }
    public int ViewportHeight { get; private set; }

    public int CullTileStartX { get; private set; }
    public int CullTileEndX   { get; private set; }
    public int CullTileStartY { get; private set; }
    public int CullTileEndY   { get; private set; }

    private int _subPixelX;
    private int _subPixelY;
    private const int MinCameraX = 24;

    public void Initialize(TileMapData map, int vw, int vh, int startX, int startY)
    {
        ViewportWidth  = vw;
        ViewportHeight = vh;
        RecalcLimits(map);
        int tx = startX - vw / 2 + vw / 6;
        int ty = startY - vh * 2 / 3;
        SetInstant(tx, ty);
        UpdateCullRange(map);
    }

    public void Update(TileMapData map)
    {
        ScrollTowardTarget();
        UpdateCullRange(map);
    }

    public void SetTarget(int worldX, int worldY)
    {
        int maxX = Math.Max(MinCameraX, MaxScrollX);
        int maxY = Math.Max(0, MaxScrollY);
        TargetX  = Math.Clamp(worldX, MinCameraX, maxX);
        TargetY  = Math.Clamp(worldY, 0, maxY);
    }

    public void SetInstant(int worldX, int worldY)
    {
        PositionX = Math.Clamp(worldX, MinCameraX, Math.Max(MinCameraX, MaxScrollX));
        PositionY = Math.Clamp(worldY, 0,           Math.Max(0, MaxScrollY));
        TargetX   = PositionX;
        TargetY   = PositionY;
        _subPixelX = _subPixelY = 0;
    }

    public void OnViewportResized(TileMapData map, int nw, int nh)
    {
        ViewportWidth  = nw;
        ViewportHeight = nh;
        RecalcLimits(map);
        ClampPosition();
        UpdateCullRange(map);
    }

    public Vec2 WorldToScreen(float wx, float wy) =>
        new(wx - PositionX, wy - PositionY);

    public Vec2I ScreenToWorld(int sx, int sy) =>
        new(sx + PositionX, sy + PositionY);

    public bool IsVisible(int wx, int wy, int w, int h) =>
        wx + w >= PositionX && wx <= PositionX + ViewportWidth &&
        wy + h >= PositionY && wy <= PositionY + ViewportHeight;

    private void RecalcLimits(TileMapData map)
    {
        MaxScrollX = Math.Max(MinCameraX, (map.TileWidth  - 1) * TileMapData.TileSize - ViewportWidth);
        MaxScrollY = Math.Max(0,          (map.TileHeight - 1) * TileMapData.TileSize - ViewportHeight);
    }

    private void ScrollTowardTarget()
    {
        if (PositionX == TargetX && PositionY == TargetY) return;
        _subPixelX += (TargetX - PositionX) * 4;
        _subPixelY += (TargetY - PositionY) * 4;
        PositionX  += _subPixelX >> 4;
        PositionY  += _subPixelY >> 4;
        _subPixelX &= 15;
        _subPixelY &= 15;
        ClampPosition();
    }

    private void ClampPosition()
    {
        PositionX = Math.Clamp(PositionX, MinCameraX, Math.Max(MinCameraX, MaxScrollX));
        PositionY = Math.Clamp(PositionY, 0,           Math.Max(0, MaxScrollY));
    }

    private void UpdateCullRange(TileMapData map)
    {
        int ts = TileMapData.TileSize;
        CullTileStartX = Math.Max(0, PositionX / ts - 1);
        CullTileStartY = Math.Max(0, PositionY / ts);
        CullTileEndX   = Math.Min(map.TileWidth,  CullTileStartX + ViewportWidth  / ts + 2);
        CullTileEndY   = Math.Min(map.TileHeight, CullTileStartY + ViewportHeight / ts + 2);
    }
}
