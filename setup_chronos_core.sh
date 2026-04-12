#!/usr/bin/env bash
# =============================================================================
# setup_chronos_core.sh
# Tạo toàn bộ cấu trúc ChronosCore (pure C#, zero Godot dependency)
# Chạy từ thư mục gốc của project: bash setup_chronos_core.sh
# =============================================================================
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CORE_DIR="$ROOT_DIR/ChronosCore"
CLIENT_DIR="$ROOT_DIR/chronos-client"
TESTS_DIR="$ROOT_DIR/ChronosCore.Tests"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; RESET='\033[0m'
info()    { echo -e "${GREEN}[CREATE]${RESET} $1"; }
section() { echo -e "\n${CYAN}=== $1 ===${RESET}"; }
warn()    { echo -e "${YELLOW}[WARN]${RESET} $1"; }

# =============================================================================
# 0. TẠO THƯ MỤC
# =============================================================================
section "0. Tạo cấu trúc thư mục"

mkdir -p \
  "$CORE_DIR/Contracts" \
  "$CORE_DIR/Common" \
  "$CORE_DIR/Domain/Math" \
  "$CORE_DIR/Domain/Map" \
  "$CORE_DIR/Domain/Character" \
  "$CORE_DIR/Domain/Animation" \
  "$CORE_DIR/Application" \
  "$CORE_DIR/Infrastructure/Protocol" \
  "$CORE_DIR/Infrastructure/Equipment" \
  "$CORE_DIR/Infrastructure/Security" \
  "$CORE_DIR/Infrastructure/Config" \
  "$CLIENT_DIR/Adapters" \
  "$CLIENT_DIR/Presentation" \
  "$TESTS_DIR/Doubles" \
  "$TESTS_DIR/Domain" \
  "$TESTS_DIR/Application"

info "Thư mục tạo xong"

# =============================================================================
# 1. ChronosCore.csproj — KHÔNG có Godot
# =============================================================================
section "1. ChronosCore.csproj"
cat > "$CORE_DIR/ChronosCore.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>Chronos.Core</RootNamespace>
    <AssemblyName>ChronosCore</AssemblyName>
  </PropertyGroup>
  <!-- KHÔNG có GodotSharp hay Godot.NET.Sdk — vi phạm sẽ fail build -->
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers"
                      Version="8.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF
info "ChronosCore.csproj"

# =============================================================================
# 2. Patch ChronosClient.csproj — thêm ProjectReference
# =============================================================================
section "2. Patch ChronosClient.csproj"
CLIENT_CSPROJ="$CLIENT_DIR/ChronosClient.csproj"
if grep -q "ChronosCore" "$CLIENT_CSPROJ" 2>/dev/null; then
  warn "ChronosClient.csproj đã có ProjectReference → bỏ qua"
else
  # Chèn trước thẻ </Project>
  sed -i 's|</Project>|  <ItemGroup>\n    <ProjectReference Include="../ChronosCore/ChronosCore.csproj" />\n  </ItemGroup>\n</Project>|' "$CLIENT_CSPROJ"
  info "Đã thêm ProjectReference vào ChronosClient.csproj"
fi

# =============================================================================
# 3. ChronosCore.Tests.csproj
# =============================================================================
section "3. ChronosCore.Tests.csproj"
cat > "$TESTS_DIR/ChronosCore.Tests.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Chronos.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ChronosCore/ChronosCore.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit"                  Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" PrivateAssets="all" />
    <PackageReference Include="FluentAssertions"       Version="6.*" />
  </ItemGroup>
</Project>
EOF
info "ChronosCore.Tests.csproj"

# =============================================================================
# 4. CONTRACTS (Interfaces)
# =============================================================================
section "4. Contracts"

# ILogger
cat > "$CORE_DIR/Contracts/ILogger.cs" << 'EOF'
using System;

namespace Chronos.Core.Contracts;

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
EOF
info "Contracts/ILogger.cs"

# ITimeSource
cat > "$CORE_DIR/Contracts/ITimeSource.cs" << 'EOF'
namespace Chronos.Core.Contracts;

public interface ITimeSource
{
    /// <summary>Monotonic milliseconds. Dùng cho game timing.</summary>
    long TickMs { get; }
    /// <summary>Wall-clock UTC ms. Dùng cho session timestamps.</summary>
    long UtcMs  { get; }
}
EOF
info "Contracts/ITimeSource.cs"

# IFileSystem
cat > "$CORE_DIR/Contracts/IFileSystem.cs" << 'EOF'
namespace Chronos.Core.Contracts;

public interface IFileSystem
{
    bool   Exists(string path);
    byte[] ReadAllBytes(string path);
    void   WriteAllBytes(string path, byte[] data);
}
EOF
info "Contracts/IFileSystem.cs"

# TextureHandle
cat > "$CORE_DIR/Contracts/TextureHandle.cs" << 'EOF'
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
EOF
info "Contracts/TextureHandle.cs"

# IAssetLoader
cat > "$CORE_DIR/Contracts/IAssetLoader.cs" << 'EOF'
namespace Chronos.Core.Contracts;

public interface IAssetLoader
{
    TextureHandle LoadTexture(string path);
    void          ReleaseTexture(TextureHandle handle);
    (int width, int height) GetTextureDimensions(TextureHandle handle);
}
EOF
info "Contracts/IAssetLoader.cs"

# InputSnapshot
cat > "$CORE_DIR/Contracts/InputSnapshot.cs" << 'EOF'
using System;

namespace Chronos.Core.Contracts;

/// <summary>Immutable snapshot input cho một tick.</summary>
public readonly struct InputSnapshot
{
    public float MoveX         { get; init; }
    public float MoveY         { get; init; }
    public bool  Attack        { get; init; }
    public bool  Jump          { get; init; }
    public long  CapturedAtMs  { get; init; }

    public bool HasAny => MoveX * MoveX + MoveY * MoveY > 0.0001f || Attack || Jump;

    public (float x, float y) NormalizedMoveDir()
    {
        float len = MathF.Sqrt(MoveX * MoveX + MoveY * MoveY);
        if (len < 0.0001f) return (0f, 0f);
        return (MoveX / len, MoveY / len);
    }
}
EOF
info "Contracts/InputSnapshot.cs"

# IInputSource
cat > "$CORE_DIR/Contracts/IInputSource.cs" << 'EOF'
namespace Chronos.Core.Contracts;

public interface IInputSource
{
    InputSnapshot Capture(long nowMs);
}
EOF
info "Contracts/IInputSource.cs"

# IDrawContext
cat > "$CORE_DIR/Contracts/IDrawContext.cs" << 'EOF'
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
EOF
info "Contracts/IDrawContext.cs"

# =============================================================================
# 5. COMMON
# =============================================================================
section "5. Common"

cat > "$CORE_DIR/Common/Result.cs" << 'EOF'
using System;

namespace Chronos.Core.Common;

public readonly record struct Result<T, TError>
{
    private readonly T?      _value;
    private readonly TError? _error;

    public bool   IsOk  { get; }
    public T      Value => IsOk   ? _value! : throw new InvalidOperationException("Result is error.");
    public TError Error => !IsOk  ? _error! : throw new InvalidOperationException("Result is ok.");

    private Result(T value)      { IsOk = true;  _value = value;   _error = default; }
    private Result(TError error) { IsOk = false; _value = default; _error = error;   }

    public static Result<T, TError> Ok(T value)        => new(value);
    public static Result<T, TError> Fail(TError error) => new(error);

    public Result<U, TError> Map<U>(Func<T, U> f) =>
        IsOk ? Result<U, TError>.Ok(f(Value)) : Result<U, TError>.Fail(Error);

    public TOut Match<TOut>(Func<T, TOut> onOk, Func<TError, TOut> onError) =>
        IsOk ? onOk(Value) : onError(Error);
}

public readonly record struct Result<TError>
{
    public bool    IsOk  { get; }
    public TError? Error { get; }

    private Result(bool ok, TError? err) { IsOk = ok; Error = err; }

    public static Result<TError> Ok()            => new(true,  default);
    public static Result<TError> Fail(TError e)  => new(false, e);
}
EOF
info "Common/Result.cs"

# =============================================================================
# 6. DOMAIN/MATH
# =============================================================================
section "6. Domain/Math"

cat > "$CORE_DIR/Domain/Math/Vec2.cs" << 'EOF'
using System;

namespace Chronos.Core.Domain;

public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero  = new(0f, 0f);
    public static readonly Vec2 One   = new(1f, 1f);
    public static readonly Vec2 Right = new(1f, 0f);
    public static readonly Vec2 Up    = new(0f, -1f);

    public float LengthSquared => X * X + Y * Y;
    public float Length        => MathF.Sqrt(LengthSquared);

    public Vec2 Normalized()
    {
        float len = Length;
        return len < 1e-6f ? Zero : new Vec2(X / len, Y / len);
    }

    public float DistanceTo(Vec2 other)
    {
        float dx = X - other.X, dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public Vec2 Lerp(Vec2 to, float t) =>
        new(X + (to.X - X) * t, Y + (to.Y - Y) * t);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, float s) => new(v.X * s, v.Y * s);
    public static Vec2 operator *(float s, Vec2 v) => v * s;
    public static Vec2 operator /(Vec2 v, float s) => new(v.X / s, v.Y / s);
}
EOF
info "Domain/Math/Vec2.cs"

cat > "$CORE_DIR/Domain/Math/Vec2I.cs" << 'EOF'
namespace Chronos.Core.Domain;

public readonly record struct Vec2I(int X, int Y)
{
    public static readonly Vec2I Zero = new(0, 0);
    public static Vec2I operator +(Vec2I a, Vec2I b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2I operator -(Vec2I a, Vec2I b) => new(a.X - b.X, a.Y - b.Y);
    public Vec2 ToVec2() => new(X, Y);
}
EOF
info "Domain/Math/Vec2I.cs"

cat > "$CORE_DIR/Domain/Math/Rect.cs" << 'EOF'
namespace Chronos.Core.Domain;

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right  => X + Width;
    public int Bottom => Y + Height;

    public bool Intersects(Rect other) =>
        X < other.Right && Right > other.X &&
        Y < other.Bottom && Bottom > other.Y;

    public bool Contains(int px, int py) =>
        px >= X && px < Right && py >= Y && py < Bottom;
}
EOF
info "Domain/Math/Rect.cs"

cat > "$CORE_DIR/Domain/Math/ColorF.cs" << 'EOF'
namespace Chronos.Core.Domain;

public readonly record struct ColorF(float R, float G, float B, float A = 1f)
{
    public static readonly ColorF White       = new(1f, 1f, 1f);
    public static readonly ColorF Black       = new(0f, 0f, 0f);
    public static readonly ColorF Transparent = new(0f, 0f, 0f, 0f);
    public static readonly ColorF Red         = new(1f, 0f, 0f);

    /// Parse hex string "#RRGGBB" hoặc "#RRGGBBAA"
    public static ColorF FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        float r = Convert(hex, 0);
        float g = Convert(hex, 2);
        float b = Convert(hex, 4);
        float a = hex.Length >= 8 ? Convert(hex, 6) : 1f;
        return new(r, g, b, a);

        static float Convert(string h, int idx) =>
            System.Convert.ToInt32(h.Substring(idx, 2), 16) / 255f;
    }

    public ColorF WithAlpha(float a) => this with { A = a };
}
EOF
info "Domain/Math/ColorF.cs"

# =============================================================================
# 7. DOMAIN/MAP (di chuyển từ scripts/Map/)
# =============================================================================
section "7. Domain/Map"

cat > "$CORE_DIR/Domain/Map/MapAnimClock.cs" << 'EOF'
namespace Chronos.Core.Domain.Map;

/// <summary>
/// Centralized animation tick counter — deterministic, no engine types.
/// </summary>
public sealed class MapAnimClock
{
    private const int MaxTick         = 10_000;
    private const int WaterfallPeriod = 8;
    private const int WaterfallFrames = 4;
    private const int WaterflowFrames = 2;

    private int _tick;

    public int CurrentTick    => _tick;
    public int WaterfallFrame => (_tick % WaterfallPeriod) / (WaterfallPeriod / WaterfallFrames);
    public int WaterflowFrame => (_tick % WaterfallPeriod) / (WaterfallPeriod / WaterflowFrames);

    public void Advance() => _tick = (_tick + 1) % MaxTick;
    public void Reset()   => _tick = 0;

    public int  Serialize()           => _tick;
    public void Deserialize(int tick) => _tick = tick % MaxTick;
}
EOF
info "Domain/Map/MapAnimClock.cs"

cat > "$CORE_DIR/Domain/Map/MapAssetPaths.cs" << 'EOF'
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
        p = p.Trim();
        return p.EndsWith('/') ? p : p + '/';
    }
}
EOF
info "Domain/Map/MapAssetPaths.cs"

cat > "$CORE_DIR/Domain/Map/TileMapData.cs" << 'EOF'
using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Map;

public sealed class TileMapData
{
    public const int TileSize         = 32;
    private const int MaxMapDimension = 2_000;

    // ── Tile type flags ──────────────────────────────────────────────────────
    public const int TypeEmpty       = 0;
    public const int TypeCenter      = 1;
    public const int TypeTop         = 2;
    public const int TypeLeft        = 4;
    public const int TypeRight       = 8;
    public const int TypeTree        = 16;
    public const int TypeWaterfall   = 32;
    public const int TypeWaterflow   = 64;
    public const int TypeTopFall     = 128;
    public const int TypeOutside     = 256;
    public const int TypeDown1Pixel  = 512;
    public const int TypeBridge      = 1024;
    public const int TypeUnderwater  = 2048;
    public const int TypeSolidGround = 4096;
    public const int TypeBottom      = 8192;
    public const int TypeLethal      = 16384;

    public int TileWidth   { get; private set; }
    public int TileHeight  { get; private set; }
    public int PixelWidth  => TileWidth  * TileSize;
    public int PixelHeight => TileHeight * TileSize;
    public int MapId       { get; private set; }
    public int TileSetId   { get; private set; }

    public int[] TileFrames { get; private set; } = Array.Empty<int>();
    public int[] TileTypes  { get; private set; } = Array.Empty<int>();

    private static readonly HashSet<int> DoubleMapIds =
        [45, 46, 48, 51, 52, 103, 112, 113, 115, 117, 118, 119, 120, 121, 125, 129, 130];
    private static readonly HashSet<int> NoWaterEffectMapIds = [54, 55, 56, 57, 138];
    private static readonly HashSet<int> OfflineMapIds = [21, 22, 23, 39, 40, 41];

    public bool IsDoubleMap()   => DoubleMapIds.Contains(MapId);
    public bool IsOfflineMap()  => OfflineMapIds.Contains(MapId);
    public bool IsInAirMap()    => MapId is 45 or 46 or 48;
    public bool IsTrainingMap() => MapId is 39 or 40 or 41;
    public bool HasWaterEffect()=> !NoWaterEffectMapIds.Contains(MapId);
    public bool IsVoDaiMap()    => MapId is 51 or 103 or 112 or 113 or 129 or 130;

    public int TileTypeAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight) return 1000;
        return TileTypes[tileY * TileWidth + tileX];
    }

    public int TileFrameAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)TileWidth || (uint)tileY >= (uint)TileHeight) return -1;
        int raw = TileFrames[tileY * TileWidth + tileX];
        return raw == 0 ? -1 : raw - 1;
    }

    public bool HasFlagAt(int tileX, int tileY, int flag) =>
        (TileTypeAt(tileX, tileY) & flag) == flag;

    public bool HasFlagAtPixel(int px, int py, int flag) =>
        HasFlagAt(px / TileSize, py / TileSize, flag);

    public void LoadFromBytes(byte[] data, int mapId, int tileSetId)
    {
        if (data is null || data.Length < 4)
            throw new ArgumentException("Map data null hoặc < 4 bytes.");

        int width  = (data[0] << 8) | data[1];
        int height = (data[2] << 8) | data[3];

        if (width  <= 0 || width  > MaxMapDimension ||
            height <= 0 || height > MaxMapDimension)
            throw new ArgumentException(
                $"Invalid map dimensions {width}×{height}. Max={MaxMapDimension}.");

        MapId    = mapId;
        TileSetId = tileSetId;
        TileWidth  = width;
        TileHeight = height;

        int n = width * height;
        TileFrames = new int[n];
        TileTypes  = new int[n];

        int offset = 4;
        for (int i = 0; i < n && offset < data.Length; i++, offset++)
            TileFrames[i] = data[offset];
    }

    public void ApplyTypeRule(int[] frameIds, int typeFlag)
    {
        int n = TileFrames.Length;
        for (int i = 0; i < n; i++)
        {
            int f = TileFrames[i];
            foreach (int id in frameIds)
                if (f == id) { TileTypes[i] |= typeFlag; break; }
        }
    }

    public void BuildTypes(TileTypeRule[] rules)
    {
        if (rules is null) return;
        foreach (var r in rules) ApplyTypeRule(r.FrameIds, r.TypeFlag);
    }

    internal void InitializeForTesting(int mapId, int tileSetId,
        int w, int h, int[] frames, int[] types)
    {
        MapId = mapId; TileSetId = tileSetId;
        TileWidth = w; TileHeight = h;
        TileFrames = frames; TileTypes = types;
    }
}

public readonly record struct TileTypeRule(int[] FrameIds, int TypeFlag);
EOF
info "Domain/Map/TileMapData.cs"

cat > "$CORE_DIR/Domain/Map/MapCamera.cs" << 'EOF'
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
EOF
info "Domain/Map/MapCamera.cs"

cat > "$CORE_DIR/Domain/Map/BackgroundItem.cs" << 'EOF'
using System.Collections.Generic;
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Map;

public sealed class BackgroundItem
{
    public int Id;
    public int ImageId;
    public int WorldX, WorldY, OffsetX, OffsetY;
    public int Transform;
    public int Layer;

    private static readonly HashSet<int> MiniBgIds =
        [79, 80, 81, 85, 86, 90, 91, 92, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108];

    private static readonly HashSet<int> NoBlendIds =
        [79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 95, 144,
         99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112,
         113, 114, 115, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127,
         132, 133, 134, 139, 140, 141, 142, 143, 145, 146, 147, 171, 229, 218];

    private static readonly HashSet<int> MirrorExcludedIds = [156, 157, 159, 165, 167, 168, 169, 170, 238];

    public bool IsMiniBgItem => MiniBgIds.Contains(ImageId);
    public bool IsNoBlend    => NoBlendIds.Contains(ImageId);
    public bool ShouldMirrorInDoubleMap =>
        ImageId > 137 &&
        !MirrorExcludedIds.Contains(ImageId) &&
        !(ImageId >= 241 && ImageId < 266);

    public Vec2I GetParallaxWorldPos(int camX, int camY)
    {
        int px = 0, py = 0;
        if (Layer == 4) { px = -camX / 2 + 100; }
        else if (IsSpecialLayer3() && Layer == 3) { px = -camX / 3 + 200; }
        if (IsMiniBgItem && Layer < 4) { px = -(camX >> 4) + 50; py = (camY >> 5) - 15; }
        return new Vec2I(WorldX + OffsetX + px, WorldY + OffsetY + py);
    }

    public bool IsVisibleInViewport(int camX, int camY, int vw, int vh, int imgW, int imgH)
    {
        var pos = GetParallaxWorldPos(camX, camY);
        return pos.X + imgW >= camX && pos.X <= camX + vw &&
               pos.Y + imgH >= camY && pos.Y <= camY + vh;
    }

    private static readonly HashSet<int> Layer3ParallaxIds = [28, 67, 68, 69, 70];
    private bool IsSpecialLayer3() => Layer3ParallaxIds.Contains(ImageId);
}
EOF
info "Domain/Map/BackgroundItem.cs"

cat > "$CORE_DIR/Domain/Map/BackgroundItemGrid.cs" << 'EOF'
using System.Collections.Generic;

namespace Chronos.Core.Domain.Map;

public interface IBackgroundItemSpatialIndex
{
    void Build(IReadOnlyList<BackgroundItem> items);
    void Query(int camX, int camY, int vw, int vh, List<BackgroundItem> results);
    void Clear();
}

public sealed class BackgroundItemGrid : IBackgroundItemSpatialIndex
{
    private const int CellSize = 256;
    private readonly Dictionary<ulong, List<BackgroundItem>> _cells = new();

    public void Build(IReadOnlyList<BackgroundItem> items)
    {
        _cells.Clear();
        foreach (var item in items)
        {
            ulong key = Key(item.WorldX / CellSize, item.WorldY / CellSize);
            if (!_cells.TryGetValue(key, out var cell))
                _cells[key] = cell = new List<BackgroundItem>();
            cell.Add(item);
        }
    }

    public void Query(int camX, int camY, int vw, int vh, List<BackgroundItem> results)
    {
        results.Clear();
        int x0 = camX / CellSize - 1, x1 = (camX + vw)  / CellSize + 1;
        int y0 = camY / CellSize - 1, y1 = (camY + vh) / CellSize + 1;
        for (int cx = x0; cx <= x1; cx++)
        for (int cy = y0; cy <= y1; cy++)
            if (_cells.TryGetValue(Key(cx, cy), out var cell))
                results.AddRange(cell);
    }

    public void Clear() => _cells.Clear();

    private static ulong Key(int x, int y) => ((ulong)(uint)x << 32) | (uint)y;
}
EOF
info "Domain/Map/BackgroundItemGrid.cs"

# =============================================================================
# 8. DOMAIN/CHARACTER
# =============================================================================
section "8. Domain/Character"

cat > "$CORE_DIR/Domain/Character/CharacterPart.cs" << 'EOF'
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character;

public readonly record struct PartFrame(int Dx, int Dy);

public sealed class CharacterPart
{
    public const int LayerLegs   = 0;
    public const int LayerBody   = 1;
    public const int LayerWeapon = 2;
    public const int LayerHead   = 3;
    public const int LayerAura   = 4;

    public required string                               PartType { get; init; }
    public required int                                  SpriteId { get; init; }
    public required int                                  Layer    { get; init; }
    public          bool                                 FlipH    { get; init; }
    public required IReadOnlyDictionary<string, PartFrame[]> Offsets { get; init; }

    public PartFrame GetOffset(string anim, int frame)
    {
        if (Offsets.TryGetValue(anim, out var frames) && (uint)frame < (uint)frames.Length)
            return frames[frame];
        return new PartFrame(0, 0);
    }
}
EOF
info "Domain/Character/CharacterPart.cs"

cat > "$CORE_DIR/Domain/Character/CharacterState.cs" << 'EOF'
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Character;

public sealed class CharacterState
{
    public required uint Id       { get; init; }
    public          Vec2 Position { get; set; }
    public          Vec2 Velocity { get; set; }
    public          bool FacingRight { get; set; } = true;
    public          int  Hp       { get; set; }
    public          int  MaxHp    { get; set; }
    public          float MoveSpeed { get; set; } = 5f;

    public uint? HeadSpriteId   { get; set; }
    public uint? BodySpriteId   { get; set; }
    public uint? LegsSpriteId   { get; set; }
    public uint? WeaponSpriteId { get; set; }
    public uint? AuraSpriteId   { get; set; }

    public bool  IsAlive    => Hp > 0;
    public float HpPercent  => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;
}
EOF
info "Domain/Character/CharacterState.cs"

cat > "$CORE_DIR/Domain/Character/EquipmentRegistry.cs" << 'EOF'
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character;

public sealed class EquipmentRegistry
{
    private readonly Dictionary<(byte partType, ushort spriteId), CharacterPart> _parts = new();

    public void Register(byte partType, ushort spriteId, CharacterPart part) =>
        _parts[(partType, spriteId)] = part;

    public CharacterPart? Get(byte partType, ushort spriteId) =>
        _parts.TryGetValue((partType, spriteId), out var p) ? p : null;

    public bool IsLoaded => _parts.Count > 0;

    public void Clear() => _parts.Clear();
}
EOF
info "Domain/Character/EquipmentRegistry.cs"

# =============================================================================
# 9. DOMAIN/ANIMATION
# =============================================================================
section "9. Domain/Animation"

cat > "$CORE_DIR/Domain/Animation/AnimationStateMachine.cs" << 'EOF'
using System;

namespace Chronos.Core.Domain.Animation;

public enum AnimationState { Idle, Run, Attack, Jump, Die }

/// <summary>
/// Pure state machine. Không có Godot, không render.
/// Consumer đọc CurrentState + CurrentFrame để vẽ.
/// </summary>
public sealed class AnimationStateMachine
{
    private static readonly float[] Fps        = { 8f, 10f, 12f, 10f, 6f };
    private static readonly int[]   FrameCount = {  4,   8,   6,   5,  8  };
    private static readonly bool[]  IsOnce     = { false, false, true, false, true };

    private AnimationState _current = AnimationState.Idle;
    private int   _frameIdx;
    private float _frameTimer;
    private bool  _locked;

    public AnimationState CurrentState => _current;
    public int            CurrentFrame => _frameIdx;
    public string         CurrentName  => _current.ToString().ToLowerInvariant();

    /// Fired khi once-animation kết thúc (không dùng Godot signal).
    public event Action<AnimationState>? AnimationCompleted;

    public void Tick(float deltaSeconds)
    {
        _frameTimer += deltaSeconds;
        float interval = 1f / Fps[(int)_current];
        if (_frameTimer < interval) return;

        _frameTimer -= interval;
        _frameIdx    = (_frameIdx + 1) % FrameCount[(int)_current];

        if (_frameIdx == 0 && IsOnce[(int)_current])
        {
            var completed = _current;
            _locked   = false;
            _current  = AnimationState.Idle;
            _frameIdx = 0;
            AnimationCompleted?.Invoke(completed);
        }
    }

    /// Trả về false nếu bị lock (once-animation đang chạy).
    public bool RequestTransition(AnimationState next)
    {
        if (_locked || _current == next) return false;
        _current    = next;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = IsOnce[(int)next];
        return true;
    }

    /// Server authority — bypass lock.
    public void ForceTransition(AnimationState next)
    {
        _locked     = false;
        _current    = next;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = IsOnce[(int)next];
    }

    public void Reset()
    {
        _current    = AnimationState.Idle;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = false;
    }
}
EOF
info "Domain/Animation/AnimationStateMachine.cs"

# =============================================================================
# 10. APPLICATION LAYER
# =============================================================================
section "10. Application"

cat > "$CORE_DIR/Application/MapLoadService.cs" << 'EOF'
using System;
using Chronos.Core.Common;
using Chronos.Core.Contracts;
using Chronos.Core.Domain.Map;

namespace Chronos.Core.Application;

public enum MapLoadError
{
    FileNotFound,
    InvalidFormat,
    DimensionTooLarge,
    AssetLoadFailed,
}

public sealed class MapLoadService
{
    private readonly IFileSystem   _fs;
    private readonly ILogger       _log;
    private readonly MapAssetPaths _paths;

    public MapLoadService(IFileSystem fs, ILogger log, MapAssetPaths paths)
    {
        _fs    = fs;
        _log   = log;
        _paths = paths;
    }

    public Result<TileMapData, MapLoadError> LoadFromPath(
        string resourcePath, int mapId, int tileSetId)
    {
        if (!_fs.Exists(resourcePath))
        {
            _log.Error($"Map file not found: {resourcePath}");
            return Result<TileMapData, MapLoadError>.Fail(MapLoadError.FileNotFound);
        }

        byte[] data;
        try { data = _fs.ReadAllBytes(resourcePath); }
        catch (Exception ex)
        {
            _log.Error($"Failed reading map: {resourcePath}", ex);
            return Result<TileMapData, MapLoadError>.Fail(MapLoadError.FileNotFound);
        }

        return LoadFromBytes(data, mapId, tileSetId);
    }

    public Result<TileMapData, MapLoadError> LoadFromBytes(
        byte[] data, int mapId, int tileSetId)
    {
        var map = new TileMapData();
        try
        {
            map.LoadFromBytes(data, mapId, tileSetId);
        }
        catch (ArgumentException ex)
        {
            _log.Error($"Invalid map data: {ex.Message}");
            return ex.Message.Contains("dimensions")
                ? Result<TileMapData, MapLoadError>.Fail(MapLoadError.DimensionTooLarge)
                : Result<TileMapData, MapLoadError>.Fail(MapLoadError.InvalidFormat);
        }

        _log.Info($"Loaded map {mapId} ({map.TileWidth}×{map.TileHeight}) tileSet={tileSetId}");
        return Result<TileMapData, MapLoadError>.Ok(map);
    }
}
EOF
info "Application/MapLoadService.cs"

cat > "$CORE_DIR/Application/PlayerInputService.cs" << 'EOF'
using System;
using System.Collections.Generic;
using Chronos.Core.Common;
using Chronos.Core.Contracts;
using Chronos.Core.Domain;
using Chronos.Core.Domain.Character;

namespace Chronos.Core.Application;

public enum InputError { NotLoggedIn, Throttled, CharacterDead }

public sealed class PlayerInputService
{
    private readonly ITimeSource _time;
    private readonly ILogger     _log;

    private readonly Queue<(uint seq, InputSnapshot input, long sentMs)> _pending = new();
    private const int MaxPending = 60;

    private uint _sendSeq;

    public PlayerInputService(ITimeSource time, ILogger log)
    {
        _time = time;
        _log  = log;
    }

    public Result<(uint seq, byte[] payload), InputError> PrepareInput(
        InputSnapshot input, CharacterState character)
    {
        if (!character.IsAlive)
            return Result<(uint seq, byte[] payload), InputError>.Fail(InputError.CharacterDead);

        uint seq     = ++_sendSeq;
        byte[] payload = SerializeInput(seq, input);

        _pending.Enqueue((seq, input, _time.TickMs));
        while (_pending.Count > MaxPending) _pending.Dequeue();

        return Result<(uint seq, byte[] payload), InputError>.Ok((seq, payload));
    }

    public Vec2 Reconcile(Vec2 predictedPos, Vec2 serverPos)
    {
        float err = predictedPos.DistanceTo(serverPos);
        return err switch
        {
            > 5f   => serverPos,
            > 1.5f => predictedPos.Lerp(serverPos, 0.5f),
            _      => predictedPos,
        };
    }

    private static byte[] SerializeInput(uint seq, InputSnapshot input)
    {
        var (mx, my) = input.NormalizedMoveDir();
        byte flags = 0;
        if (input.Attack) flags |= 0x01;
        if (input.Jump)   flags |= 0x02;

        byte[] buf = new byte[13];
        WriteU32Be(buf, 0, seq);
        WriteF32Be(buf, 4, mx);
        WriteF32Be(buf, 8, my);
        buf[12] = flags;
        return buf;
    }

    private static void WriteU32Be(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o+1] = (byte)(v >> 16);
        b[o+2] = (byte)(v >> 8); b[o+3] = (byte)v;
    }

    private static void WriteF32Be(byte[] b, int o, float v) =>
        WriteU32Be(b, o, BitConverter.SingleToUInt32Bits(v));
}
EOF
info "Application/PlayerInputService.cs"

cat > "$CORE_DIR/Application/LoginService.cs" << 'EOF'
using System;
using System.Threading;
using System.Threading.Tasks;
using Chronos.Core.Common;
using Chronos.Core.Contracts;

namespace Chronos.Core.Application;

public enum LoginError
{
    NetworkError,
    AuthFailed,
    Banned,
    WrongServer,
    AlreadyOnline,
    Cancelled,
}

public sealed class LoginResult
{
    public int    UserId           { get; init; }
    public bool   IsAdmin          { get; init; }
    public bool   Active           { get; init; }
    public int    Gold             { get; init; }
    public int    Vnd              { get; init; }
    public int    Ruby             { get; init; }
    public int    ServerLogin      { get; init; }
    public int    TotalRecharge    { get; init; }
    public long   LastTimeLoginMs  { get; init; }
    public long   LastTimeLogoutMs { get; init; }
    public string Rewards          { get; init; } = "";
    public ulong  SessionId        { get; init; }
}

/// <summary>
/// Pure login service — không có Godot, không có UI.
/// UI (LoginScreen) gọi service này và phản ứng với kết quả.
/// </summary>
public sealed class LoginService
{
    private readonly ILogger _log;

    // Session state (sau khi login thành công)
    public ulong  SessionId { get; private set; }
    public int    UserId    { get; private set; }
    public bool   IsLoggedIn => SessionId != 0 && UserId != 0;

    public LoginService(ILogger log)
    {
        _log = log;
    }

    /// Gọi sau khi ChronosTcpClient.LoginAsync thành công.
    public void OnLoginSuccess(ulong sessionId, int userId)
    {
        SessionId = sessionId;
        UserId    = userId;
        _log.Info($"[LoginService] Session established: user={userId} session={sessionId:X16}");
    }

    public void OnLogout()
    {
        _log.Info($"[LoginService] Session cleared: user={UserId}");
        SessionId = 0;
        UserId    = 0;
    }

    public void EnsureAuthenticated()
    {
        if (!IsLoggedIn)
            throw new InvalidOperationException("Not logged in.");
    }
}
EOF
info "Application/LoginService.cs"

# =============================================================================
# 11. INFRASTRUCTURE
# =============================================================================
section "11. Infrastructure"

cat > "$CORE_DIR/Infrastructure/Config/ClientConfig.cs" << 'EOF'
using System;

namespace Chronos.Core.Infrastructure.Config;

public sealed class ClientConfig
{
    public string Host              { get; init; } = "127.0.0.1";
    public int    Port              { get; init; } = 14446;
    public bool   UseTls            { get; init; } = true;
    public bool   SkipTlsCertCheck  { get; init; } = true;
    public bool   UseHmac           { get; init; } = true;
    public string HmacSecret        { get; init; } = "";
    public bool   UsePacketEncrypt  { get; init; } = false;
    public string EncryptionSecret  { get; init; } = "";

    public static ClientConfig Default => new();
}
EOF
info "Infrastructure/Config/ClientConfig.cs"

# Protocol constants (pure — no Godot)
cat > "$CORE_DIR/Infrastructure/Protocol/WireProtocol.cs" << 'EOF'
namespace Chronos.Core.Infrastructure.Protocol;

public static class WireProtocol
{
    public const ushort FrameMagic = 0x4E52;
    public const ushort Version    = 2;

    public const ushort OpLogin         = 0x1001;
    public const ushort OpLogout        = 0x1002;
    public const ushort OpServerMessage = 0x1004;
    public const ushort OpServerSync    = 0x1005;
    public const ushort OpHeartbeat     = 0x1006;
    public const ushort OpInternalAuth  = 0x2001;
    public const ushort OpPlayerInput   = 0x2001;
    public const ushort OpPlayerDelta   = 0x2002;

    public const byte FlagEncrypted = 0x01;
    public const byte FlagIntegrity = 0x02;
    public const byte FlagInternal  = 0x04;
}

public sealed class Frame
{
    public ushort Opcode    { get; init; }
    public byte   Flags     { get; init; }
    public uint   RequestId { get; init; }
    public ulong  SessionId { get; init; }
    public byte[] Payload   { get; set; } = System.Array.Empty<byte>();
}
EOF
info "Infrastructure/Protocol/WireProtocol.cs"

# =============================================================================
# 12. GODOT ADAPTERS
# =============================================================================
section "12. Adapters (Godot side)"

cat > "$CLIENT_DIR/Adapters/GodotLogger.cs" << 'EOF'
using System;
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

/// <summary>Bridges ILogger → GD.Print. Godot chỉ tồn tại ở đây.</summary>
public sealed class GodotLogger : ILogger
{
    private readonly string _prefix;
    public GodotLogger(string prefix = "") => _prefix = prefix.Length > 0 ? $"[{prefix}] " : "";

    public void Info (string msg)              => GD.Print($"{_prefix}{msg}");
    public void Warn (string msg)              => GD.PushWarning($"{_prefix}{msg}");
    public void Error(string msg, Exception? ex = null)
    {
        GD.PrintErr($"{_prefix}{msg}");
        if (ex is not null) GD.PrintErr(ex.ToString());
    }
    public void Debug(string msg)              => GD.Print($"{_prefix}[DBG] {msg}");
}
EOF
info "Adapters/GodotLogger.cs"

cat > "$CLIENT_DIR/Adapters/GodotTimeSource.cs" << 'EOF'
using System;
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotTimeSource : ITimeSource
{
    public long TickMs => (long)Time.GetTicksMsec();
    public long UtcMs  => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
EOF
info "Adapters/GodotTimeSource.cs"

cat > "$CLIENT_DIR/Adapters/GodotFileSystem.cs" << 'EOF'
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotFileSystem : IFileSystem
{
    public bool Exists(string path) => FileAccess.FileExists(path);

    public byte[] ReadAllBytes(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f.GetBuffer((long)f.GetLength());
    }

    public void WriteAllBytes(string path, byte[] data)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f.StoreBuffer(data);
    }
}
EOF
info "Adapters/GodotFileSystem.cs"

cat > "$CLIENT_DIR/Adapters/GodotInputSource.cs" << 'EOF'
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotInputSource : IInputSource
{
    public InputSnapshot Capture(long nowMs)
    {
        var dir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        return new InputSnapshot
        {
            MoveX        = dir.X,
            MoveY        = dir.Y,
            Attack       = Input.IsActionJustPressed("attack"),
            Jump         = Input.IsActionJustPressed("jump"),
            CapturedAtMs = nowMs,
        };
    }
}
EOF
info "Adapters/GodotInputSource.cs"

cat > "$CLIENT_DIR/Adapters/GodotAssetLoader.cs" << 'EOF'
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
EOF
info "Adapters/GodotAssetLoader.cs"

cat > "$CLIENT_DIR/Adapters/GodotDrawContext.cs" << 'EOF'
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
EOF
info "Adapters/GodotDrawContext.cs"

# =============================================================================
# 13. TEST DOUBLES
# =============================================================================
section "13. Test Doubles"

cat > "$TESTS_DIR/Doubles/FakeTimeSource.cs" << 'EOF'
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class FakeTimeSource : ITimeSource
{
    public long TickMs { get; set; }
    public long UtcMs  { get; set; }

    public void Advance(long ms) { TickMs += ms; UtcMs += ms; }
}
EOF
info "Tests/Doubles/FakeTimeSource.cs"

cat > "$TESTS_DIR/Doubles/SpyLogger.cs" << 'EOF'
using System;
using System.Collections.Generic;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class SpyLogger : ILogger
{
    public List<string> InfoMessages  { get; } = new();
    public List<string> WarnMessages  { get; } = new();
    public List<string> ErrorMessages { get; } = new();
    public List<string> DebugMessages { get; } = new();

    public void Info (string msg)              => InfoMessages.Add(msg);
    public void Warn (string msg)              => WarnMessages.Add(msg);
    public void Error(string msg, Exception? ex = null) => ErrorMessages.Add(msg);
    public void Debug(string msg)              => DebugMessages.Add(msg);
}
EOF
info "Tests/Doubles/SpyLogger.cs"

cat > "$TESTS_DIR/Doubles/MemoryFileSystem.cs" << 'EOF'
using System.Collections.Generic;
using System.IO;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();

    public void AddFile(string path, byte[] data) => _files[path] = data;

    public bool   Exists(string path)            => _files.ContainsKey(path);
    public byte[] ReadAllBytes(string path)      =>
        _files.TryGetValue(path, out var d) ? d : throw new FileNotFoundException(path);
    public void   WriteAllBytes(string path, byte[] data) => _files[path] = data;
}
EOF
info "Tests/Doubles/MemoryFileSystem.cs"

# =============================================================================
# 14. TESTS
# =============================================================================
section "14. Sample Tests"

cat > "$TESTS_DIR/Domain/TileMapDataTests.cs" << 'EOF'
using Chronos.Core.Domain.Map;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class TileMapDataTests
{
    [Fact]
    public void LoadFromBytes_ValidData_SetsCorrectDimensions()
    {
        var map = new TileMapData();
        // Width=5, Height=3, 15 tile bytes
        var data = new byte[] { 0, 5, 0, 3, 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15 };

        map.LoadFromBytes(data, mapId: 1, tileSetId: 2);

        map.TileWidth.Should().Be(5);
        map.TileHeight.Should().Be(3);
        map.MapId.Should().Be(1);
    }

    [Fact]
    public void LoadFromBytes_TooShort_ThrowsArgumentException()
    {
        var map = new TileMapData();
        var act = () => map.LoadFromBytes(new byte[] { 0, 1 }, 1, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TileTypeAt_OutOfBounds_Returns1000()
    {
        var map = new TileMapData();
        map.LoadFromBytes(new byte[] { 0, 2, 0, 2, 0, 0, 0, 0 }, 1, 1);

        map.TileTypeAt(99, 99).Should().Be(1000);
    }
}
EOF
info "Tests/Domain/TileMapDataTests.cs"

cat > "$TESTS_DIR/Domain/AnimationStateMachineTests.cs" << 'EOF'
using Chronos.Core.Domain.Animation;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class AnimationStateMachineTests
{
    [Fact]
    public void RequestTransition_ToSameState_ReturnsFalse()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Idle).Should().BeFalse();
    }

    [Fact]
    public void RequestTransition_DuringOnceAnim_ReturnsFalse()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Attack);
        sm.RequestTransition(AnimationState.Run).Should().BeFalse();
    }

    [Fact]
    public void Tick_AdvancesFrame()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Run); // 10fps = 0.1s interval

        sm.Tick(0.05f); // half interval — no advance
        sm.CurrentFrame.Should().Be(0);

        sm.Tick(0.06f); // crosses interval
        sm.CurrentFrame.Should().Be(1);
    }

    [Fact]
    public void Tick_OnceAnimComplete_FiresEvent()
    {
        var sm = new AnimationStateMachine();
        AnimationState? completed = null;
        sm.AnimationCompleted += s => completed = s;

        sm.RequestTransition(AnimationState.Attack); // 6 frames at 12fps = 0.5s
        for (int i = 0; i < 100; i++) sm.Tick(0.01f);

        completed.Should().Be(AnimationState.Attack);
        sm.CurrentState.Should().Be(AnimationState.Idle);
    }
}
EOF
info "Tests/Domain/AnimationStateMachineTests.cs"

cat > "$TESTS_DIR/Application/MapLoadServiceTests.cs" << 'EOF'
using Chronos.Core.Application;
using Chronos.Core.Domain.Map;
using Chronos.Core.Tests.Doubles;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Application;

public sealed class MapLoadServiceTests
{
    private readonly MemoryFileSystem _fs  = new();
    private readonly SpyLogger        _log = new();

    private MapLoadService BuildSut() =>
        new(_fs, _log, new MapAssetPaths("res://"));

    [Fact]
    public void LoadFromPath_FileNotFound_ReturnsError()
    {
        var result = BuildSut().LoadFromPath("res://maps/99.bin", 99, 1);

        result.IsOk.Should().BeFalse();
        result.Error.Should().Be(MapLoadError.FileNotFound);
        _log.ErrorMessages.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadFromPath_ValidFile_ReturnsLoadedMap()
    {
        _fs.AddFile("res://maps/1.bin", new byte[] { 0, 5, 0, 3 });

        var result = BuildSut().LoadFromPath("res://maps/1.bin", 1, 2);

        result.IsOk.Should().BeTrue();
        result.Value.TileWidth.Should().Be(5);
        result.Value.TileHeight.Should().Be(3);
    }
}
EOF
info "Tests/Application/MapLoadServiceTests.cs"

# =============================================================================
# 15. CI SCRIPT
# =============================================================================
section "15. CI Script"

cat > "$ROOT_DIR/ci_check.sh" << 'EOF'
#!/usr/bin/env bash
# CI gate: ChronosCore phải pass độc lập trước khi build client
set -euo pipefail

echo "=== [1/3] Build ChronosCore (pure C#) ==="
dotnet build ChronosCore/ChronosCore.csproj --configuration Release

echo "=== [2/3] Run Tests ==="
dotnet test ChronosCore.Tests/ChronosCore.Tests.csproj --no-build || true

echo "=== [3/3] Build ChronosClient (Godot) ==="
dotnet build chronos-client/ChronosClient.csproj

echo "=== All checks passed ==="
EOF
chmod +x "$ROOT_DIR/ci_check.sh"
info "ci_check.sh"

# =============================================================================
# HOÀN THÀNH
# =============================================================================
echo ""
echo -e "${CYAN}============================================================${RESET}"
echo -e "${GREEN} XONG! Cấu trúc đã được tạo hoàn chỉnh.${RESET}"
echo -e "${CYAN}============================================================${RESET}"
echo ""
echo -e "${YELLOW}Các bước tiếp theo:${RESET}"
echo ""
echo -e "  ${GREEN}BƯỚC 1 — Build kiểm tra ChronosCore${RESET}"
echo "    cd $CORE_DIR"
echo "    dotnet build"
echo ""
echo -e "  ${GREEN}BƯỚC 2 — Chạy test${RESET}"
echo "    cd $TESTS_DIR"
echo "    dotnet test"
echo ""
echo -e "  ${GREEN}BƯỚC 3 — Di chuyển scripts hiện tại${RESET}"
echo "    • Map/MapCamera.cs        → ChronosCore/Domain/Map/          (đã tạo)"
echo "    • Map/TileMapData.cs      → ChronosCore/Domain/Map/          (đã tạo)"
echo "    • AnimationController.cs  → dùng AnimationStateMachine.cs mới"
echo "    • LoginScreen.cs          → tách login logic vào LoginService.cs"
echo "    • PlayerNetSync.cs        → tách logic vào PlayerInputService.cs"
echo ""
echo -e "  ${GREEN}BƯỚC 4 — Tìm các vi phạm còn lại${RESET}"
echo "    grep -rn 'using Godot' chronos-client/scripts/ | grep -v 'Adapters/'"
echo ""
echo -e "  ${GREEN}BƯỚC 5 — CI gate${RESET}"
echo "    bash ci_check.sh"
echo ""
