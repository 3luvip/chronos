---
# CHRONOS — PURE C# ENGINE-INDEPENDENT SKILL
## Strict Engine-Independent Mode: Production Reference

> **Prime directive**: Every class in the Domain and Application layers must compile  
> without referencing the Godot assembly. Zero engine types. Zero inheritance from Node.  
> The engine is a runtime host, not a foundation.
---
---

## TABLE OF CONTENTS

1. [The Engine-Independent Boundary](#1-the-engine-independent-boundary)
2. [Compilation Enforcement](#2-compilation-enforcement)
3. [Interface Contract Catalogue](#3-interface-contract-catalogue)
4. [Adapter Layer Architecture](#4-adapter-layer-architecture)
5. [Pure Domain: Data Models](#5-pure-domain-data-models)
6. [Pure Domain: State Machines](#6-pure-domain-state-machines)
7. [Pure Domain: Map & Tile Logic](#7-pure-domain-map--tile-logic)
8. [Pure Domain: Character & Equipment](#8-pure-domain-character--equipment)
9. [Pure Application: Game Loop Services](#9-pure-application-game-loop-services)
10. [Pure Application: Network Protocol](#10-pure-application-network-protocol)
11. [Pure Infrastructure: Binary Serialization](#11-pure-infrastructure-binary-serialization)
12. [Pure Infrastructure: Crypto & Security](#12-pure-infrastructure-crypto--security)
13. [Pure Infrastructure: Configuration](#13-pure-infrastructure-configuration)
14. [C# Language Rules for Pure Layers](#14-c-language-rules-for-pure-layers)
15. [Testing Pure C# in Isolation](#15-testing-pure-c-in-isolation)
16. [Migration Guide: Contaminated → Pure](#16-migration-guide-contaminated--pure)
17. [File & Namespace Layout](#17-file--namespace-layout)

---

## 1. THE ENGINE-INDEPENDENT BOUNDARY

### The Hard Line

```
PURE C# (no engine assembly reference)           ENGINE BOUNDARY
─────────────────────────────────────────────────────────────────
Domain/                                          Presentation/
  TileMapData.cs          ────────────────►       MapRenderer.cs
  CharacterPart.cs                                  └── IMapRenderer adapter
  AnimationStateMachine.cs                        
Application/                                     Presentation/
  GameLoopService.cs      ────────────────►       GameScreen.cs
  PlayerInputService.cs                             └── IInputSource adapter
  MapLoadService.cs                               
Infrastructure/                                  Presentation/
  ChronosTcpClient.cs     ────────────────►       NetworkBridge.cs
  BinEquipLoader.cs                                 └── IAssetLoader adapter
  PacketCrypto.cs                                   └── IFileSystem adapter
─────────────────────────────────────────────────────────────────
         Pure C# compiles here                 Godot lives here only
```

### What "Pure" Means Exactly

A class is pure if its `.cs` file can be compiled with only:

```xml
<ItemGroup>
  <PackageReference Include="System.Memory" Version="4.5.5" />
  <!-- No Godot.NET.Sdk -->
  <!-- No GodotSharp.dll reference -->
</ItemGroup>
```

and produces zero errors.

### What Immediately Violates Purity

| Violation | Marker |
|---|---|
| `using Godot;` | [ENGINE CONTAMINATION] |
| `: Node`, `: Node2D`, `: Control` | [ENGINE CONTAMINATION] |
| `GD.Load<T>()`, `GD.Print()` | [ENGINE CONTAMINATION] |
| `Vector2`, `Vector2I`, `Color`, `Rect2` | [ENGINE CONTAMINATION] — use own structs |
| `[Export]`, `[Signal]` attributes | [ENGINE CONTAMINATION] |
| `GetNode<T>()`, `AddChild()` | [ENGINE CONTAMINATION] |
| `ResourceLoader.*` | [ENGINE CONTAMINATION] |
| `Time.GetTicksMsec()` | [ENGINE CONTAMINATION] — use `DateTimeOffset` |

---

## 2. COMPILATION ENFORCEMENT

### Separate Project File

Create `ChronosCore.csproj` alongside `ChronosClient.csproj`:

```xml
<!-- ChronosCore.csproj — pure C#, zero engine dependency -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Hard prohibition: any Godot reference fails the build -->
    <NoWarn></NoWarn>
  </PropertyGroup>

  <!-- Explicitly list allowed references — nothing else -->
  <ItemGroup>
    <!-- System packages only -->
  </ItemGroup>

  <!-- Analyzer to catch engine leakage at compile time -->
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`ChronosClient.csproj` references `ChronosCore.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\ChronosCore\ChronosCore.csproj" />
</ItemGroup>
```

### CI Gate

```bash
# In CI pipeline — must pass independently:
dotnet build ChronosCore/ChronosCore.csproj --configuration Release
dotnet test  ChronosCore.Tests/               # pure unit tests, no engine
dotnet build ChronosClient.csproj            # engine build, references core
```

If `ChronosCore` build fails, the client build must not be attempted. The core is the contract.

---

## 3. INTERFACE CONTRACT CATALOGUE

Every engine-dependent capability the pure layers need is expressed as an interface in the core. The engine implements those interfaces in the Presentation layer. The core never references the implementation.

### 3.1 ILogger

```csharp
// ChronosCore/Contracts/ILogger.cs
namespace Chronos.Core.Contracts;

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
```

**Never** use `GD.Print()`, `Console.WriteLine()`, or `System.Diagnostics.Debug` inside core. Inject `ILogger`.

### 3.2 ITimeSource

```csharp
// ChronosCore/Contracts/ITimeSource.cs
namespace Chronos.Core.Contracts;

public interface ITimeSource
{
    /// Monotonic milliseconds since some fixed epoch.
    /// Use for game timing — does NOT need to match wall clock.
    long TickMs { get; }

    /// Wall-clock UTC milliseconds. Use for session timestamps only.
    long UtcMs { get; }
}
```

Godot adapter:

```csharp
// ChronosClient/Adapters/GodotTimeSource.cs
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotTimeSource : ITimeSource
{
    public long TickMs => (long)Time.GetTicksMsec();
    public long UtcMs  => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
```

### 3.3 IFileSystem

```csharp
// ChronosCore/Contracts/IFileSystem.cs
namespace Chronos.Core.Contracts;

public interface IFileSystem
{
    bool Exists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] data);
}
```

### 3.4 ITextureHandle

The core never touches a texture object. It works with opaque integer handles.

```csharp
// ChronosCore/Contracts/ITextureHandle.cs
namespace Chronos.Core.Contracts;

/// Opaque handle to a loaded texture. The integer ID is the only thing
/// the pure layer knows about a texture. The Presentation layer maps IDs
/// to engine texture objects.
public readonly record struct TextureHandle(int Id)
{
    public static readonly TextureHandle None = new(0);
    public bool IsValid => Id != 0;
}
```

### 3.5 IAssetLoader

```csharp
// ChronosCore/Contracts/IAssetLoader.cs
namespace Chronos.Core.Contracts;

public interface IAssetLoader
{
    /// Loads a texture and returns an opaque handle. Returns TextureHandle.None on failure.
    TextureHandle LoadTexture(string path);

    /// Releases the texture identified by handle. Safe to call with None.
    void ReleaseTexture(TextureHandle handle);

    /// Returns pixel dimensions. Returns (0, 0) for invalid handles.
    (int width, int height) GetTextureDimensions(TextureHandle handle);
}
```

### 3.6 IInputSnapshot

```csharp
// ChronosCore/Contracts/IInputSnapshot.cs
namespace Chronos.Core.Contracts;

/// Immutable snapshot of player input for one tick.
/// Taken by the Presentation layer, passed into the pure Application layer.
public readonly struct InputSnapshot
{
    public float MoveX    { get; init; }
    public float MoveY    { get; init; }
    public bool  Attack   { get; init; }
    public bool  Jump     { get; init; }
    public long  CapturedAtMs { get; init; }

    public bool HasAny =>
        MoveX * MoveX + MoveY * MoveY > 0.0001f || Attack || Jump;

    /// Normalizes move vector to max length 1.0.
    public (float x, float y) NormalizedMoveDir()
    {
        float len = MathF.Sqrt(MoveX * MoveX + MoveY * MoveY);
        if (len < 0.0001f) return (0f, 0f);
        return (MoveX / len, MoveY / len);
    }
}
```

### 3.7 IDrawContext

The pure rendering pipeline issues draw commands through an abstraction, never touching engine draw APIs directly.

```csharp
// ChronosCore/Contracts/IDrawContext.cs
namespace Chronos.Core.Contracts;

public interface IDrawContext
{
    void DrawTexture(TextureHandle handle, int screenX, int screenY);
    void DrawTextureRegion(
        TextureHandle handle,
        int screenX,   int screenY,
        int srcX,      int srcY,
        int srcWidth,  int srcHeight,
        int dstWidth,  int dstHeight);
    void DrawTextureFlippedH(TextureHandle handle, int screenX, int screenY, int width);
    void SetTransform(float scaleX, float scaleY, float tx, float ty);
    void ResetTransform();
}
```

Godot adapter in `MapRenderer._Draw()`:

```csharp
// ChronosClient/Adapters/GodotDrawContext.cs
using Chronos.Core.Contracts;
using Godot;

namespace Chronos.Client.Adapters;

public sealed class GodotDrawContext : IDrawContext
{
    private readonly CanvasItem  _canvas;
    private readonly TextureBank _bank;     // maps TextureHandle → Texture2D

    public GodotDrawContext(CanvasItem canvas, TextureBank bank)
    {
        _canvas = canvas;
        _bank   = bank;
    }

    public void DrawTexture(TextureHandle handle, int screenX, int screenY)
    {
        var tex = _bank.Resolve(handle);
        if (tex is null) return;
        _canvas.DrawTexture(tex, new Vector2(screenX, screenY));
    }

    public void DrawTextureRegion(TextureHandle handle,
        int screenX, int screenY, int srcX, int srcY,
        int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        var tex = _bank.Resolve(handle);
        if (tex is null) return;
        var src = new Rect2(srcX, srcY, srcWidth, srcHeight);
        var dst = new Rect2(screenX, screenY, dstWidth, dstHeight);
        _canvas.DrawTextureRectRegion(tex, dst, src);
    }

    // ... remaining methods
}
```

---

## 4. ADAPTER LAYER ARCHITECTURE

### 4.1 Object Graph Assembly

The Presentation layer (Godot Node) owns the object graph. It constructs pure-layer objects and injects adapters. Pure objects never construct themselves.

```csharp
// ChronosClient/Presentation/MapRenderer.cs  (Godot side)
public partial class MapRenderer : Node2D
{
    // Pure core objects — no engine types inside
    private MapRenderPipeline _pipeline = null!;
    private MapAssetManager   _assets   = null!;

    // Adapters — bridge between core and engine
    private GodotAssetLoader  _loader   = null!;
    private GodotTimeSource   _time     = null!;

    public override void _Ready()
    {
        _time    = new GodotTimeSource();
        _loader  = new GodotAssetLoader(ResourceBasePath);
        _assets  = new MapAssetManager(_loader, new MapAssetPaths(ResourceBasePath));
        _pipeline = new MapRenderPipeline(_assets, _time);

        // Wire layers — all pure objects
        _pipeline.RegisterLayer(new BackgroundParallaxLayer(4, zOrder: 10, _assets));
        _pipeline.RegisterLayer(new TileLayer(_assets, _pipeline.AnimClock));
        // ...
    }

    public override void _Process(double delta)
    {
        _pipeline.Tick();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var ctx = new GodotDrawContext(this, _loader.Bank);
        _pipeline.Draw(ctx);
    }
}
```

### 4.2 Adapter Naming Convention

| Pure interface | Godot adapter | Test double |
|---|---|---|
| `ILogger` | `GodotLogger` | `NullLogger`, `SpyLogger` |
| `ITimeSource` | `GodotTimeSource` | `FakeTimeSource` |
| `IFileSystem` | `GodotFileSystem` | `MemoryFileSystem` |
| `IAssetLoader` | `GodotAssetLoader` | `FakeAssetLoader` |
| `IDrawContext` | `GodotDrawContext` | `RecordingDrawContext` |
| `IInputSource` | `GodotInputSource` | `ScriptedInputSource` |

### 4.3 No Adapter Logic in Adapters

Adapters translate types. They contain zero business logic:

```csharp
// CORRECT adapter — pure translation:
public sealed class GodotFileSystem : IFileSystem
{
    public bool Exists(string path)         => FileAccess.FileExists(path);
    public byte[] ReadAllBytes(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f.GetBuffer((long)f.GetLength());
    }
    public void WriteAllBytes(string path, byte[] data) { /* ... */ }
}

// WRONG — business logic in adapter:
public sealed class GodotFileSystem : IFileSystem
{
    public byte[] ReadAllBytes(string path)
    {
        if (!FileAccess.FileExists(path))
            throw new MapLoadException($"Map file missing: {path}"); // WRONG — logic belongs in MapLoadService
        var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var bytes = f.GetBuffer((long)f.GetLength());
        if (bytes.Length < 4)
            throw new MapLoadException("File too short");             // WRONG — validation in core
        return bytes;
    }
}
```

---

## 5. PURE DOMAIN: DATA MODELS

### 5.1 Own Math Structs

The core defines its own value types. No dependency on Godot's `Vector2`:

```csharp
// ChronosCore/Domain/Math/Vec2.cs
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
}
```

```csharp
// ChronosCore/Domain/Math/Vec2I.cs
namespace Chronos.Core.Domain;

public readonly record struct Vec2I(int X, int Y)
{
    public static readonly Vec2I Zero = new(0, 0);
    public static Vec2I operator +(Vec2I a, Vec2I b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2I operator -(Vec2I a, Vec2I b) => new(a.X - b.X, a.Y - b.Y);
}
```

```csharp
// ChronosCore/Domain/Math/Rect.cs
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
```

```csharp
// ChronosCore/Domain/Math/ColorF.cs
namespace Chronos.Core.Domain;

public readonly record struct ColorF(float R, float G, float B, float A = 1f)
{
    public static readonly ColorF White       = new(1f, 1f, 1f);
    public static readonly ColorF Transparent = new(0f, 0f, 0f, 0f);

    public ColorF WithAlpha(float a) => new(R, G, B, a);

    public static ColorF FromHex(uint hex)
    {
        float r = ((hex >> 24) & 0xFF) / 255f;
        float g = ((hex >> 16) & 0xFF) / 255f;
        float b = ((hex >>  8) & 0xFF) / 255f;
        float a = ((hex >>  0) & 0xFF) / 255f;
        return new(r, g, b, a);
    }
}
```

### 5.2 Record vs Class vs Struct Decision Matrix

| Type | Use When |
|---|---|
| `readonly record struct` | Value semantics, size ≤ 32 bytes, frequently compared (Vec2, Rect, TextureHandle) |
| `record class` | Immutable reference type, needs inheritance or null (domain events) |
| `sealed class` | Mutable service objects, complex lifecycle, never subclassed |
| `struct` (non-record) | Extreme performance paths; only when profiler confirms |
| `abstract class` | Base for domain entity hierarchies (use sparingly) |

### 5.3 Pure TileMapData

Current `TileMapData` in the codebase imports `Godot` only for `GD.PrintErr()` and `FileAccess`. Strip these:

```csharp
// ChronosCore/Domain/Map/TileMapData.cs
using System;
using System.Collections.Generic;
using Chronos.Core.Contracts;  // ILogger, IFileSystem — injected

namespace Chronos.Core.Domain.Map;

public sealed class TileMapData
{
    public const int TileSize        = 32;
    private const int MaxMapDimension = 2_000;

    public int TileWidth  { get; private set; }
    public int TileHeight { get; private set; }
    public int PixelWidth  => TileWidth  * TileSize;
    public int PixelHeight => TileHeight * TileSize;
    public int MapId      { get; private set; }
    public int TileSetId  { get; private set; }

    public int[] TileFrames { get; private set; } = Array.Empty<int>();
    public int[] TileTypes  { get; private set; } = Array.Empty<int>();

    // Map category queries — no engine dependency
    public bool IsDoubleMap()   => _doubleMapIds.Contains(MapId);
    public bool IsOfflineMap()  => _offlineMapIds.Contains(MapId);
    public bool IsInAirMap()    => MapId is 45 or 46 or 48;
    public bool HasWaterEffect()=> !_noWaterEffectMapIds.Contains(MapId);

    private static readonly HashSet<int> _doubleMapIds =
        [45, 46, 48, 51, 52, 103, 112, 113, 115, 117, 118, 119, 120, 121, 125, 129, 130];
    private static readonly HashSet<int> _noWaterEffectMapIds = [54, 55, 56, 57, 138];
    private static readonly HashSet<int> _offlineMapIds = [21, 22, 23, 39, 40, 41];

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

    /// Throws ArgumentException on invalid data — never returns null.
    /// Caller (infrastructure layer) handles exceptions and translates to user-facing errors.
    public void LoadFromBytes(byte[] data, int mapId, int tileSetId)
    {
        if (data is null || data.Length < 4)
            throw new ArgumentException("Map data is null or truncated (< 4 bytes).");

        int width  = (data[0] << 8) | data[1];
        int height = (data[2] << 8) | data[3];

        if (width  <= 0 || width  > MaxMapDimension ||
            height <= 0 || height > MaxMapDimension)
            throw new ArgumentException(
                $"Invalid map dimensions {width}×{height}. Max is {MaxMapDimension}.");

        MapId    = mapId;
        TileSetId = tileSetId;
        TileWidth  = width;
        TileHeight = height;

        int cellCount = width * height;
        TileFrames = new int[cellCount];
        TileTypes  = new int[cellCount];

        int offset = 4;
        for (int i = 0; i < cellCount && offset < data.Length; i++, offset++)
            TileFrames[i] = data[offset];
    }

    public void ApplyTypeRule(int[] frameIds, int typeFlag)
    {
        int cellCount = TileFrames.Length;
        for (int i = 0; i < cellCount; i++)
        {
            int frame = TileFrames[i];
            foreach (int id in frameIds)
            {
                if (frame == id) { TileTypes[i] |= typeFlag; break; }
            }
        }
    }

    public void BuildTypes(TileTypeRule[] rules)
    {
        if (rules is null) return;
        foreach (var rule in rules) ApplyTypeRule(rule.FrameIds, rule.TypeFlag);
    }

    // Test/factory bypass — never used in production path
    internal void InitializeForTesting(
        int mapId, int tileSetId, int w, int h, int[] frames, int[] types)
    {
        MapId = mapId; TileSetId = tileSetId;
        TileWidth = w; TileHeight = h;
        TileFrames = frames; TileTypes = types;
    }
}

public readonly record struct TileTypeRule(int[] FrameIds, int TypeFlag);
```

---

## 6. PURE DOMAIN: STATE MACHINES

### 6.1 Enum-Driven State Machine Pattern

Current `AnimationController` mixes animation timing with state machine logic inside a Godot `Node`. Extract the state machine to pure C#:

```csharp
// ChronosCore/Domain/Animation/AnimationStateMachine.cs
namespace Chronos.Core.Domain.Animation;

public enum AnimationState { Idle, Run, Attack, Jump, Die }

/// Pure animation state machine. No engine types, no rendering.
/// The consumer reads CurrentState + CurrentFrame and issues draw calls separately.
public sealed class AnimationStateMachine
{
    // Per-state config — index by (int)AnimationState
    private static readonly float[] Fps        = { 8f, 10f, 12f, 10f, 6f };
    private static readonly int[]   FrameCount = {  4,   8,   6,   5,  8  };
    private static readonly bool[]  IsOnce     = { false, false, true, false, true };

    private AnimationState _current = AnimationState.Idle;
    private int            _frameIdx;
    private float          _frameTimer;
    private bool           _locked; // true during once-animations

    public AnimationState CurrentState => _current;
    public int            CurrentFrame => _frameIdx;

    /// Fired when a once-animation completes. Observer pattern — no engine signals.
    public event Action<AnimationState>? AnimationCompleted;

    public void Tick(float deltaSeconds)
    {
        _frameTimer += deltaSeconds;
        float interval = 1f / Fps[(int)_current];

        if (_frameTimer < interval) return;

        _frameTimer -= interval;
        _frameIdx    = (_frameIdx + 1) % FrameCount[(int)_current];

        bool cycleComplete = _frameIdx == 0;
        if (cycleComplete && IsOnce[(int)_current])
        {
            var completed = _current;
            _locked  = false;
            _current = AnimationState.Idle;
            _frameIdx = 0;
            AnimationCompleted?.Invoke(completed);
        }
    }

    /// Returns false if transition was rejected (locked once-animation in progress).
    public bool RequestTransition(AnimationState next)
    {
        if (_locked || _current == next) return false;

        _current    = next;
        _frameIdx   = 0;
        _frameTimer = 0f;
        _locked     = IsOnce[(int)next];
        return true;
    }

    /// Server authority — bypasses lock. Use only for server-commanded state changes.
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
```

### 6.2 Functional State Machine for Complex States

For future NPC AI or multi-phase game states, use a discriminated union pattern:

```csharp
// ChronosCore/Domain/Npc/NpcState.cs
namespace Chronos.Core.Domain.Npc;

public abstract record NpcState
{
    public sealed record Idle(long IdleSinceMs)             : NpcState;
    public sealed record Patrolling(Vec2 Destination)       : NpcState;
    public sealed record Chasing(uint TargetPlayerId)       : NpcState;
    public sealed record Attacking(uint TargetPlayerId, long LastAttackMs) : NpcState;
    public sealed record Dead(long DiedAtMs)                : NpcState;
}

public sealed class NpcBrain
{
    private NpcState _state = new NpcState.Idle(0);

    public NpcState CurrentState => _state;

    public void Tick(long nowMs, NpcContext ctx)
    {
        _state = _state switch
        {
            NpcState.Idle idle       => TickIdle(idle, nowMs, ctx),
            NpcState.Patrolling p    => TickPatrolling(p, nowMs, ctx),
            NpcState.Chasing c       => TickChasing(c, nowMs, ctx),
            NpcState.Attacking a     => TickAttacking(a, nowMs, ctx),
            NpcState.Dead            => _state, // terminal state
            _                        => _state,
        };
    }

    private NpcState TickIdle(NpcState.Idle idle, long nowMs, NpcContext ctx)
    {
        if (ctx.NearestPlayerDistance < ctx.AggroRange)
            return new NpcState.Chasing(ctx.NearestPlayerId);
        if (nowMs - idle.IdleSinceMs > 5_000)
            return new NpcState.Patrolling(ctx.NextPatrolPoint);
        return idle;
    }

    // ... other tick methods
}
```

---

## 7. PURE DOMAIN: MAP & TILE LOGIC

### 7.1 Pure MapCamera

Current `MapCamera` references only `System` — it is already pure. Enforce this with the project boundary. No changes required except ensuring it stays in `ChronosCore`.

Key rules to preserve:

```csharp
// Sub-pixel accumulator — fixed-point arithmetic, no floats in position:
private int _subPixelAccumulatorX;
private int _subPixelAccumulatorY;

private void ScrollTowardTarget()
{
    _subPixelAccumulatorX += (TargetX - PositionX) * 4;
    _subPixelAccumulatorY += (TargetY - PositionY) * 4;
    PositionX             += _subPixelAccumulatorX >> 4;
    PositionY             += _subPixelAccumulatorY >> 4;
    _subPixelAccumulatorX &= 15;
    _subPixelAccumulatorY &= 15;
    ClampCameraPosition();
}
```

This is integer-only smooth scrolling. Never convert to `float` — it will introduce platform-dependent rounding and break future determinism.

### 7.2 Pure Spatial Index

`BackgroundItemGrid` uses only `System.Collections.Generic`. Keep pure. The `ulong` key packing:

```csharp
// No boxing: (int, int) tuple would box on every dictionary lookup.
// ulong packing: zero allocation, O(1) hash.
private static ulong EncodeCellKey(int cellX, int cellY) =>
    ((ulong)(uint)cellX << 32) | (uint)cellY;
```

This is the required pattern for all 2D spatial keys in the core. Never use `ValueTuple<int,int>` as a dictionary key — it boxes when used as `object` in non-generic paths.

### 7.3 Pure MapAnimClock

```csharp
// ChronosCore/Domain/Map/MapAnimClock.cs
namespace Chronos.Core.Domain.Map;

/// Tick counter shared across all render layers.
/// Drives waterfall, waterflow, and future animation systems.
/// DETERMINISTIC: advancing by N ticks from state S always produces the same result.
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

    /// Serializable state for rollback / replay.
    public int Serialize()           => _tick;
    public void Deserialize(int tick) => _tick = tick % MaxTick;
}
```

---

## 8. PURE DOMAIN: CHARACTER & EQUIPMENT

### 8.1 Pure CharacterPart

Current `CharacterPart` uses `System.Collections.Generic` only — already pure. Enforce the record pattern:

```csharp
// ChronosCore/Domain/Character/CharacterPart.cs
using System.Collections.Generic;
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Character;

public readonly record struct PartFrame(int Dx, int Dy);

public sealed class CharacterPart
{
    public required string PartType  { get; init; }  // "head","body","legs","weapon","aura"
    public required int    SpriteId  { get; init; }
    public required int    Layer     { get; init; }
    public bool            FlipH     { get; init; }

    /// Key: animation name, Value: frame offsets array.
    public required IReadOnlyDictionary<string, PartFrame[]> Offsets { get; init; }

    public PartFrame GetOffset(string anim, int frame)
    {
        if (Offsets.TryGetValue(anim, out var frames) && (uint)frame < (uint)frames.Length)
            return frames[frame];
        return new PartFrame(0, 0);
    }

    // Sorted layer constants — document here, not scattered in code:
    public const int LayerLegs   = 0;
    public const int LayerBody   = 1;
    public const int LayerWeapon = 2;
    public const int LayerHead   = 3;
    public const int LayerAura   = 4;
}
```

### 8.2 Pure Equipment Registry

```csharp
// ChronosCore/Domain/Character/EquipmentRegistry.cs
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character;

/// In-memory registry of all loaded equipment parts.
/// Populated by infrastructure layer (BinEquipLoader). Never modified at runtime.
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
```

### 8.3 Pure CharacterState

```csharp
// ChronosCore/Domain/Character/CharacterState.cs
using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Character;

/// Full authoritative state of one character (player or NPC).
/// Mutable — updated by server delta or local prediction.
public sealed class CharacterState
{
    public required uint   Id        { get; init; }
    public          Vec2   Position  { get; set; }
    public          Vec2   Velocity  { get; set; }
    public          bool   FacingRight { get; set; } = true;
    public          int    Hp        { get; set; }
    public          int    MaxHp     { get; set; }
    public          float  MoveSpeed { get; set; } = 5f;  // tiles/s

    // Equipment — nullable means slot is empty
    public uint? HeadSpriteId   { get; set; }
    public uint? BodySpriteId   { get; set; }
    public uint? LegsSpriteId   { get; set; }
    public uint? WeaponSpriteId { get; set; }
    public uint? AuraSpriteId   { get; set; }

    public bool IsAlive => Hp > 0;

    public float HpPercent => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;
}
```

---

## 9. PURE APPLICATION: GAME LOOP SERVICES

### 9.1 Service Design Rules

```
Service rules:
1. Constructor injection only — no service locator, no static access.
2. All time comes from ITimeSource — never DateTime.Now or Godot.Time.
3. All logging goes to ILogger — never Console.Write or GD.Print.
4. Methods return Result<T, TError> for operations that can fail.
5. Services are sealed — composition over inheritance.
6. Services are not Nodes — they have no _Ready, no _Process.
7. The engine calls services via adapters; services never call engine.
```

### 9.2 Result Type

```csharp
// ChronosCore/Common/Result.cs
namespace Chronos.Core.Common;

public readonly record struct Result<T, TError>
{
    private readonly T?      _value;
    private readonly TError? _error;

    public bool  IsOk  { get; }
    public T     Value => IsOk ? _value! : throw new InvalidOperationException("Result is error.");
    public TError Error => !IsOk ? _error! : throw new InvalidOperationException("Result is ok.");

    private Result(T value)      { IsOk = true;  _value = value; _error = default; }
    private Result(TError error) { IsOk = false; _value = default; _error = error; }

    public static Result<T, TError> Ok(T value)       => new(value);
    public static Result<T, TError> Fail(TError error) => new(error);

    public Result<U, TError> Map<U>(Func<T, U> f) =>
        IsOk ? Result<U, TError>.Ok(f(Value)) : Result<U, TError>.Fail(Error);

    public TOut Match<TOut>(Func<T, TOut> onOk, Func<TError, TOut> onError) =>
        IsOk ? onOk(Value) : onError(Error);
}

// Convenience alias for void success
public readonly record struct Result<TError>
{
    public bool   IsOk  { get; }
    public TError? Error { get; }

    private Result(bool ok, TError? err) { IsOk = ok; Error = err; }
    public static Result<TError> Ok()           => new(true,  default);
    public static Result<TError> Fail(TError e) => new(false, e);
}
```

### 9.3 Pure PlayerInputService

```csharp
// ChronosCore/Application/PlayerInputService.cs
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
    private readonly ITimeSource    _time;
    private readonly ILogger        _log;

    // Client-side prediction history: seq → (snapshot, timestamp)
    private readonly Queue<(uint seq, InputSnapshot input, long sentMs)> _pending = new();
    private const int MaxPending = 60;  // ~1.2s at 50Hz

    private uint _sendSeq;

    public PlayerInputService(ITimeSource time, ILogger log)
    {
        _time = time;
        _log  = log;
    }

    /// Prepares an input packet for transmission.
    /// Returns the serialized payload or an error — does not transmit itself.
    public Result<(uint seq, byte[] payload), InputError> PrepareInput(
        InputSnapshot input,
        CharacterState character)
    {
        if (!character.IsAlive)
            return Result<(uint seq, byte[] payload), InputError>.Fail(InputError.CharacterDead);

        uint seq = ++_sendSeq;
        byte[] payload = SerializeInput(seq, input);

        // Record for reconciliation
        _pending.Enqueue((seq, input, _time.TickMs));
        while (_pending.Count > MaxPending) _pending.Dequeue();

        return Result<(uint seq, byte[] payload), InputError>.Ok((seq, payload));
    }

    /// Applies a server position correction to predicted position.
    public Vec2 Reconcile(Vec2 predictedPos, Vec2 serverPos)
    {
        float error = predictedPos.DistanceTo(serverPos);

        return error switch
        {
            > 5f    => serverPos,                             // teleport snap
            > 1.5f  => predictedPos.Lerp(serverPos, 0.5f),  // smooth correction
            _       => predictedPos,                          // within tolerance
        };
    }

    private static byte[] SerializeInput(uint seq, InputSnapshot input)
    {
        // Big-endian: u32 seq | f32 moveX | f32 moveY | u8 flags
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

    private static void WriteU32Be(byte[] buf, int off, uint v)
    {
        buf[off]   = (byte)(v >> 24);
        buf[off+1] = (byte)(v >> 16);
        buf[off+2] = (byte)(v >>  8);
        buf[off+3] = (byte) v;
    }

    private static void WriteF32Be(byte[] buf, int off, float v)
    {
        uint bits = BitConverter.SingleToUInt32Bits(v);
        WriteU32Be(buf, off, bits);
    }
}
```

### 9.4 Pure MapLoadService

```csharp
// ChronosCore/Application/MapLoadService.cs
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
    private readonly IFileSystem _fs;
    private readonly ILogger     _log;
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
            _log.Error($"Failed reading map file: {resourcePath}", ex);
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
            // Distinguish dimension error from format error
            return ex.Message.Contains("dimensions")
                ? Result<TileMapData, MapLoadError>.Fail(MapLoadError.DimensionTooLarge)
                : Result<TileMapData, MapLoadError>.Fail(MapLoadError.InvalidFormat);
        }

        _log.Info($"Loaded map {mapId} ({map.TileWidth}×{map.TileHeight}) tileSet={tileSetId}");
        return Result<TileMapData, MapLoadError>.Ok(map);
    }
}
```

---

## 10. PURE APPLICATION: NETWORK PROTOCOL

### 10.1 Pure PacketWriter / PacketReader

Current `Protocol.cs` is already pure C# — no engine imports. Enforce and extend:

```csharp
// ChronosCore/Infrastructure/Protocol/PacketWriter.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Chronos.Core.Infrastructure.Protocol;

/// Stack-friendly packet builder. Uses MemoryStream internally.
/// For zero-allocation hot paths, use ArrayBufferWriter<byte> + Span<byte> overloads.
public sealed class PacketWriter
{
    private readonly MemoryStream _ms = new();

    public void WriteByte(byte v)  => _ms.WriteByte(v);
    public void WriteBool(bool v)  => _ms.WriteByte(v ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        _ms.Write(b);
    }

    public void WriteInt32(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        _ms.Write(b);
    }

    public void WriteUInt32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        _ms.Write(b);
    }

    public void WriteInt64(long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        _ms.Write(b);
    }

    public void WriteUInt64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        _ms.Write(b);
    }

    /// Writes 2-byte length prefix + UTF-8 bytes.
    public void WriteUtf(string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        if (encoded.Length > ushort.MaxValue)
            throw new InvalidOperationException($"String too long: {encoded.Length} bytes.");
        WriteUInt16((ushort)encoded.Length);
        _ms.Write(encoded);
    }

    public byte[] ToArray() => _ms.ToArray();

    /// Zero-copy path for large payloads: write directly into caller-provided buffer.
    public int CopyTo(Span<byte> destination)
    {
        var bytes = _ms.GetBuffer();
        int len   = (int)_ms.Length;
        bytes.AsSpan(0, len).CopyTo(destination);
        return len;
    }
}
```

### 10.2 Zero-Allocation Packet Decoder

```csharp
// ChronosCore/Infrastructure/Protocol/SpanPacketReader.cs
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chronos.Core.Infrastructure.Protocol;

/// Zero-allocation packet reader. Operates on ReadOnlySpan<byte>.
/// Use this for all hot-path decoding (network receive loop, batch delta processing).
public ref struct SpanPacketReader
{
    private ReadOnlySpan<byte> _data;
    private int                _pos;

    public SpanPacketReader(ReadOnlySpan<byte> data) { _data = data; _pos = 0; }

    public int Remaining => _data.Length - _pos;
    public bool IsEmpty  => _pos >= _data.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        EnsureRemaining(1);
        return _data[_pos++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16()
    {
        EnsureRemaining(2);
        var v = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_pos, 2));
        _pos += 2;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        EnsureRemaining(4);
        var v = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32()
    {
        EnsureRemaining(4);
        var v = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        EnsureRemaining(8);
        var v = BinaryPrimitives.ReadInt64BigEndian(_data.Slice(_pos, 8));
        _pos += 8;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64()
    {
        EnsureRemaining(8);
        var v = BinaryPrimitives.ReadUInt64BigEndian(_data.Slice(_pos, 8));
        _pos += 8;
        return v;
    }

    /// Allocates a string. Acceptable for login/logout packets — not for 50Hz game packets.
    public string ReadUtf()
    {
        ushort len = ReadUInt16();
        EnsureRemaining(len);
        var s = Encoding.UTF8.GetString(_data.Slice(_pos, len));
        _pos += len;
        return s;
    }

    /// Zero-allocation: returns a span slice instead of a string.
    /// Caller must not store the span beyond the lifetime of the source buffer.
    public ReadOnlySpan<byte> ReadRawUtfBytes()
    {
        ushort len = ReadUInt16();
        EnsureRemaining(len);
        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureRemaining(int needed)
    {
        if (_pos + needed > _data.Length)
            throw new InvalidOperationException(
                $"Packet underflow: need {needed} bytes, have {_data.Length - _pos}.");
    }
}
```

---

## 11. PURE INFRASTRUCTURE: BINARY SERIALIZATION

### 11.1 BinEquipLoader — Purified

Current `BinEquipLoader` uses `System.IO.File.ReadAllBytes` (acceptable) and `System.Runtime.InteropServices.Marshal` (acceptable — no engine). It is pure. Maintain this.

Add an `IFileSystem`-injectable overload for testability:

```csharp
// ChronosCore/Infrastructure/Equipment/EquipmentLoader.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chronos.Core.Contracts;
using Chronos.Core.Domain.Character;

namespace Chronos.Core.Infrastructure.Equipment;

public static class EquipmentLoader
{
    private const uint   EqpMagic  = 0x43484E52u;
    private const ushort FormatVer = 1;

    /// Loads from IFileSystem — fully testable with MemoryFileSystem.
    public static EquipmentRegistry LoadRegistry(string path, IFileSystem fs)
    {
        byte[] raw = fs.ReadAllBytes(path);
        var parts  = LoadParts(raw);
        var registry = new EquipmentRegistry();

        foreach (var (spriteId, loaded) in parts)
        {
            var offsets = BuildOffsets(loaded.Anims);
            var part = new CharacterPart
            {
                PartType = MapPartType(loaded.PartType),
                SpriteId = (int)spriteId,
                Layer    = loaded.Layer,
                Offsets  = offsets,
            };
            registry.Register(loaded.PartType, (ushort)spriteId, part);
        }

        return registry;
    }

    private static Dictionary<string, PartFrame[]> BuildOffsets(
        Dictionary<byte, LoadedFrameOffset[]> anims)
    {
        var result = new Dictionary<string, PartFrame[]>(anims.Count);
        foreach (var (animId, frames) in anims)
        {
            string name = animId switch
            {
                0 => "idle", 1 => "run", 2 => "attack",
                3 => "jump", 4 => "die",
                _ => $"anim_{animId}",
            };
            var pf = new PartFrame[frames.Length];
            for (int i = 0; i < frames.Length; i++)
                pf[i] = new PartFrame(frames[i].Dx, frames[i].Dy);
            result[name] = pf;
        }
        return result;
    }

    private static string MapPartType(byte t) => t switch
    {
        0 => "legs", 1 => "body", 2 => "weapon", 3 => "head", 4 => "aura",
        _ => $"part_{t}",
    };

    // Internal structs and parsing logic omitted for brevity —
    // mirror the existing BinEquipLoader struct layout exactly.
}
```

### 11.2 Memory Safety in Struct Casting

When using `MemoryMarshal.Read<T>()`, the struct must be `[StructLayout(LayoutKind.Sequential, Pack = 1)]` and the source byte count must be validated:

```csharp
// CORRECT — size validated before cast:
if (recBytes.Length < Marshal.SizeOf<PartRecord>())
    throw new InvalidDataException("Truncated PartRecord.");
var rec = MemoryMarshal.Read<PartRecord>(recBytes);

// WRONG — undefined behavior if buffer is short:
var rec = MemoryMarshal.Read<PartRecord>(someBytes); // no size check
```

This rule applies to every `MemoryMarshal.Read<T>()` call. No exceptions.

---

## 12. PURE INFRASTRUCTURE: CRYPTO & SECURITY

### 12.1 PacketCrypto — Already Pure

`PacketCrypto.cs` is pure C#. It uses only:
- `System.Security.Cryptography` (AesGcm, HMACSHA256, RandomNumberGenerator, Rfc2898DeriveBytes)
- `System.Buffers.Binary.BinaryPrimitives`
- `System.Text.Encoding`

Maintain this. Never add a `using Godot;` to this file.

### 12.2 Key Zeroing Pattern

```csharp
// CORRECT — zero key material on dispose:
public void Dispose()
{
    if (!_disposed)
    {
        _disposed = true;
        Array.Clear(_aesKey, 0, _aesKey.Length);
        Array.Clear(_xorKey, 0, _xorKey.Length);
        // Note: GC.Collect() is NOT called here.
        // Array.Clear() zeroes the managed bytes immediately.
        // GC timing does not affect security because the bytes are already zeroed.
    }
}
```

### 12.3 Constant-Time Comparison

```csharp
// ChronosCore/Infrastructure/Security/CryptoUtils.cs
using System;
using System.Security.Cryptography;

namespace Chronos.Core.Infrastructure.Security;

public static class CryptoUtils
{
    /// Timing-safe byte comparison. Use for all HMAC tag verification.
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) =>
        CryptographicOperations.FixedTimeEquals(a, b);

    /// Generates a cryptographically secure session ID.
    public static ulong GenerateSessionId()
    {
        Span<byte> buf = stackalloc byte[8];
        RandomNumberGenerator.Fill(buf);
        ulong id = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(buf);
        return id == 0 ? 1 : id; // 0 is reserved for "no session"
    }

    /// Derives a key using PBKDF2-SHA256. Never use fewer than 100_000 iterations.
    public static byte[] DeriveKey(string secret, string salt, int keyLength, int iterations = 100_000)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(salt),
            iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(keyLength);
    }
}
```

---

## 13. PURE INFRASTRUCTURE: CONFIGURATION

### 13.1 Config Model

```csharp
// ChronosCore/Infrastructure/Config/ClientConfig.cs
namespace Chronos.Core.Infrastructure.Config;

/// Immutable client configuration. Loaded once at startup.
/// No Godot ProjectSettings dependency — read from environment or ini file.
public sealed class ClientConfig
{
    public string  ServerHost           { get; init; } = "127.0.0.1";
    public int     ServerPort           { get; init; } = 14446;
    public bool    UseTls               { get; init; } = true;
    public bool    SkipTlsCertValidation{ get; init; } = false; // FALSE in production
    public bool    UseHmac              { get; init; } = true;
    public string  HmacSecret           { get; init; } = "";
    public bool    UsePacketEncryption  { get; init; } = false;
    public string  EncryptionSecret     { get; init; } = "";
    public int     HeartbeatIntervalMs  { get; init; } = 30_000;

    /// Validates all required fields. Returns error string or null on success.
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerHost))    return "ServerHost is required.";
        if (ServerPort is < 1 or > 65535)             return "ServerPort out of range.";
        if (UseHmac && string.IsNullOrEmpty(HmacSecret)) return "HmacSecret required when UseHmac=true.";
        if (UsePacketEncryption && string.IsNullOrEmpty(EncryptionSecret))
            return "EncryptionSecret required when UsePacketEncryption=true.";
        if (SkipTlsCertValidation && UseTls)
            return "[WARN] SkipTlsCertValidation=true — not safe for production.";
        return null;
    }

    public static ClientConfig FromEnvironment()
    {
        return new ClientConfig
        {
            ServerHost            = Env("CHRONOS_SERVER_HOST", "127.0.0.1"),
            ServerPort            = EnvInt("CHRONOS_SERVER_PORT", 14446),
            UseTls                = EnvBool("CHRONOS_TLS", true),
            SkipTlsCertValidation = EnvBool("CHRONOS_SKIP_TLS_VERIFY", false),
            UseHmac               = EnvBool("CHRONOS_HMAC", true),
            HmacSecret            = Env("CHRONOS_HMAC_SECRET", ""),
            UsePacketEncryption   = EnvBool("CHRONOS_ENCRYPT_PACKETS", false),
            EncryptionSecret      = Env("CHRONOS_ENCRYPT_SECRET", ""),
            HeartbeatIntervalMs   = EnvInt("CHRONOS_HEARTBEAT_MS", 30_000),
        };
    }

    private static string Env(string key, string def) =>
        System.Environment.GetEnvironmentVariable(key) ?? def;
    private static int EnvInt(string key, int def) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(key), out int v) ? v : def;
    private static bool EnvBool(string key, bool def) =>
        System.Environment.GetEnvironmentVariable(key) is { } s
            ? s is "1" or "true" or "TRUE" or "yes"
            : def;
}
```

---

## 14. C# LANGUAGE RULES FOR PURE LAYERS

### 14.1 Nullability

All pure C# files must have `#nullable enable` (or set globally in the project file). Rules:

```csharp
// REQUIRED at file top (or in .csproj):
#nullable enable

// CORRECT — explicit nullable intent:
public CharacterPart? Get(byte partType, ushort spriteId) { ... }

// WRONG — nullable without annotation (compiler warning):
public CharacterPart Get(byte partType, ushort spriteId) { ... } // may return null silently
```

Never suppress nullable warnings with `!` (null-forgiving) unless you have provably verified the reference is non-null, and you add a comment explaining why.

### 14.2 Records for Value Objects

Use `readonly record struct` for all domain value objects that:
- Have value equality semantics
- Are size ≤ 32 bytes
- Are immutable

```csharp
// CORRECT:
public readonly record struct PartFrame(int Dx, int Dy);
public readonly record struct TextureHandle(int Id);
public readonly record struct Vec2(float X, float Y);

// WRONG for value objects — class allocates, has reference equality:
public class PartFrame { public int Dx; public int Dy; } // [PERFORMANCE ISSUE]
```

### 14.3 Span and Memory for Hot Paths

```csharp
// Tier 1 — Zero allocation. Use for all packet reading in game loop:
void ProcessBatch(ReadOnlySpan<byte> payload)
{
    var reader = new SpanPacketReader(payload);
    ushort count = reader.ReadUInt16();
    // ...
}

// Tier 2 — Single allocation. Acceptable for once-per-login operations:
void ProcessLogin(byte[] payload)
{
    using var ms = new MemoryStream(payload, writable: false);
    // ...
}

// Tier 3 — Multiple allocations. Only for startup/config:
void LoadConfig(string path) { /* File.ReadAllBytes etc. */ }
```

The game loop (50Hz) must use Tier 1 for all packet processing.

### 14.4 Switch Expressions Over Switch Statements

```csharp
// CORRECT — exhaustive pattern matching with compiler enforcement:
string MapPartType(byte t) => t switch
{
    0 => "legs", 1 => "body", 2 => "weapon", 3 => "head", 4 => "aura",
    _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown part type."),
};

// WRONG for domain logic — not exhaustive, easy to miss new cases:
switch (t) {
    case 0: return "legs";
    // ... developer forgets a case, returns null silently
}
```

### 14.5 Forbidden Patterns in Pure C#

| Pattern | Reason | Alternative |
|---|---|---|
| `Thread.Sleep()` | Blocks — use async | `await Task.Delay()` |
| `Console.WriteLine()` | Not injected | `ILogger.Info()` |
| `System.Diagnostics.Debug.WriteLine()` | Not injected | `ILogger.Debug()` |
| `Environment.Exit()` | Kills engine | Return error result |
| `GC.Collect()` | Don't force GC | Let runtime decide |
| `lock(this)` | Anti-pattern | `lock(_syncRoot)` with private object |
| Public mutable fields | Breaks encapsulation | Properties with `private set` or `init` |
| `static` mutable state without documentation | Hidden coupling | Document lifetime explicitly |
| `dynamic` | Bypasses type system, boxes | Generic methods |
| `object` as value container | Boxes value types | Generic containers |

### 14.6 Exception Philosophy

```csharp
// In Domain — throw on programming errors (should never happen in correct code):
public int TileFrameAt(int x, int y) {
    // Out of bounds returns -1, not exception. Caller handles -1 correctly.
    if ((uint)x >= (uint)TileWidth) return -1;
    // ...
}

// In Infrastructure — return Result<T, E> for expected failures:
public Result<TileMapData, MapLoadError> LoadFromPath(string path) {
    if (!_fs.Exists(path))
        return Result<TileMapData, MapLoadError>.Fail(MapLoadError.FileNotFound);
    // ...
}

// In Application — convert infrastructure errors to user-facing domain errors:
public async Task<LoginResult> LoginAsync(string username, string password) {
    // Catch network exceptions, return LoginResult.Failed("Connection error")
    // Never let IOException propagate to Presentation layer
}
```

### 14.7 Async Rules

```csharp
// CORRECT — CancellationToken threaded through the entire call chain:
public async Task<LoginResult> LoginAsync(
    string username, string password, CancellationToken ct)
{
    await _client.ConnectAsync(_config.ServerHost, _config.ServerPort, ct);
    return await _client.LoginAsync(username, password, ct);
}

// WRONG — fire-and-forget without cancellation:
public async void DoLogin() { ... } // [MEMORY RISK] exceptions swallowed, no cancellation

// CORRECT for event handlers that must be async (rare):
private async void OnButtonPressed_LoginAsync()
{
    try   { await DoLoginAsync(_cts.Token); }
    catch (OperationCanceledException) { /* normal */ }
    catch (Exception ex) { _log.Error("Login failed", ex); }
}
```

`async void` is permitted only at the Presentation layer event handler boundary. Never in Domain, Application, or Infrastructure.

---

## 15. TESTING PURE C# IN ISOLATION

### 15.1 Test Project Setup

```xml
<!-- ChronosCore.Tests/ChronosCore.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk"  Version="17.*" />
    <PackageReference Include="xunit"                    Version="2.*"  />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions"          Version="6.*"  />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ChronosCore\ChronosCore.csproj" />
  </ItemGroup>
</Project>
```

No Godot dependency. Tests run with `dotnet test` on any machine.

### 15.2 Test Doubles

```csharp
// ChronosCore.Tests/Doubles/FakeTimeSource.cs
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class FakeTimeSource : ITimeSource
{
    public long TickMs { get; set; }
    public long UtcMs  { get; set; }

    public void Advance(long ms) { TickMs += ms; UtcMs += ms; }
}
```

```csharp
// ChronosCore.Tests/Doubles/MemoryFileSystem.cs
using System.Collections.Generic;
using System.IO;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();

    public void AddFile(string path, byte[] data) => _files[path] = data;

    public bool     Exists(string path)        => _files.ContainsKey(path);
    public byte[]   ReadAllBytes(string path)  =>
        _files.TryGetValue(path, out var d) ? d : throw new FileNotFoundException(path);
    public void     WriteAllBytes(string path, byte[] data) => _files[path] = data;
}
```

```csharp
// ChronosCore.Tests/Doubles/SpyLogger.cs
using System.Collections.Generic;
using Chronos.Core.Contracts;

namespace Chronos.Core.Tests.Doubles;

public sealed class SpyLogger : ILogger
{
    public List<string> InfoMessages  = new();
    public List<string> WarnMessages  = new();
    public List<string> ErrorMessages = new();

    public void Info(string m)                     => InfoMessages.Add(m);
    public void Warn(string m)                     => WarnMessages.Add(m);
    public void Error(string m, Exception? ex = null) => ErrorMessages.Add(m);
    public void Debug(string m)                    { }
}
```

### 15.3 Sample Unit Tests

```csharp
// ChronosCore.Tests/Domain/TileMapDataTests.cs
using FluentAssertions;
using Chronos.Core.Domain.Map;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class TileMapDataTests
{
    [Fact]
    public void LoadFromBytes_ValidData_SetsCorrectDimensions()
    {
        byte[] data = { 0, 10, 0, 5 }; // width=10, height=5, zero tiles
        var map = new TileMapData();

        map.LoadFromBytes(data, mapId: 1, tileSetId: 3);

        map.TileWidth.Should().Be(10);
        map.TileHeight.Should().Be(5);
        map.PixelWidth.Should().Be(320);
    }

    [Fact]
    public void LoadFromBytes_TooLargeDimension_Throws()
    {
        byte[] data = { 0xFF, 0xFF, 0xFF, 0xFF }; // 65535×65535 — exceeds limit
        var map = new TileMapData();

        var act = () => map.LoadFromBytes(data, 1, 1);

        act.Should().Throw<ArgumentException>().WithMessage("*dimensions*");
    }

    [Fact]
    public void TileTypeAt_OutOfBounds_Returns1000()
    {
        var map = new TileMapData();
        map.LoadFromBytes(new byte[] { 0, 2, 0, 2, 1, 0, 0, 1 }, 1, 1);

        map.TileTypeAt(99, 99).Should().Be(1000);
        map.TileTypeAt(-1, 0).Should().Be(1000);
    }
}
```

```csharp
// ChronosCore.Tests/Domain/AnimationStateMachineTests.cs
using FluentAssertions;
using Chronos.Core.Domain.Animation;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class AnimationStateMachineTests
{
    [Fact]
    public void RequestTransition_WhileLocked_ReturnsFalse()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Attack); // locks

        bool result = sm.RequestTransition(AnimationState.Run);

        result.Should().BeFalse();
        sm.CurrentState.Should().Be(AnimationState.Attack);
    }

    [Fact]
    public void ForceTransition_WhileLocked_Succeeds()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Attack);

        sm.ForceTransition(AnimationState.Die);

        sm.CurrentState.Should().Be(AnimationState.Die);
    }

    [Fact]
    public void Tick_AdvancesFrameByTime()
    {
        var sm = new AnimationStateMachine();
        // Run at 10fps → interval = 0.1s
        sm.RequestTransition(AnimationState.Run);

        sm.Tick(0.05f); // half interval — no advance
        sm.CurrentFrame.Should().Be(0);

        sm.Tick(0.05f); // full interval — advance
        sm.CurrentFrame.Should().Be(1);
    }

    [Fact]
    public void Tick_OnceAnimationComplete_FiresEvent()
    {
        var sm = new AnimationStateMachine();
        AnimationState? completed = null;
        sm.AnimationCompleted += s => completed = s;

        sm.RequestTransition(AnimationState.Attack); // 6 frames at 12fps = 0.5s
        for (int i = 0; i < 100; i++) sm.Tick(0.01f); // 1 second — enough to complete

        completed.Should().Be(AnimationState.Attack);
        sm.CurrentState.Should().Be(AnimationState.Idle);
    }
}
```

```csharp
// ChronosCore.Tests/Application/MapLoadServiceTests.cs
using FluentAssertions;
using Chronos.Core.Application;
using Chronos.Core.Domain.Map;
using Chronos.Core.Infrastructure.Config;
using Chronos.Core.Tests.Doubles;
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
        var sut = BuildSut();

        var result = sut.LoadFromPath("res://maps/99.bin", mapId: 99, tileSetId: 1);

        result.IsOk.Should().BeFalse();
        result.Error.Should().Be(MapLoadError.FileNotFound);
        _log.ErrorMessages.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadFromPath_ValidFile_ReturnsLoadedMap()
    {
        _fs.AddFile("res://maps/1.bin", new byte[] { 0, 5, 0, 3 });
        var sut = BuildSut();

        var result = sut.LoadFromPath("res://maps/1.bin", mapId: 1, tileSetId: 2);

        result.IsOk.Should().BeTrue();
        result.Value.TileWidth.Should().Be(5);
        result.Value.TileHeight.Should().Be(3);
    }
}
```
### 15.3 Sample Unit Tests

---

## 16. MIGRATION GUIDE: CONTAMINATED → PURE

### Step-by-step for any existing class

**Step 1 — Audit the using directives**

```bash
# Find all files with Godot contamination:
grep -rn "using Godot" scripts/ | grep -v "Adapters/"
```

For each hit, classify each engine usage:
- Engine type in a field → replace with pure equivalent or interface
- Engine API call → move to adapter method

**Step 2 — Extract the interface**

```csharp
// Before — contaminated:
public partial class MapCamera : Node
{
    private Vector2 _position; // Godot type

    public void Update(double delta)
    {
        // Uses Godot Vector2 arithmetic
    }
}

// After Step 2 — interface extracted:
namespace Chronos.Core.Domain.Map;

public interface IMapCamera
{
    int PositionX { get; }
    int PositionY { get; }
    void SetTarget(int worldX, int worldY);
    void Update(TileMapData map);
}
```

**Step 3 — Implement pure class**

```csharp
// Pure implementation — no using Godot:
namespace Chronos.Core.Domain.Map;

public sealed class MapCamera : IMapCamera
{
    public int PositionX { get; private set; }
    public int PositionY { get; private set; }
    // ... uses Vec2I, int — no engine types
}
```

**Step 4 — Create engine adapter (only if needed)**

If Presentation layer needs to bridge Godot types to the pure interface:

```csharp
// ChronosClient/Adapters/GodotCameraAdapter.cs
using Godot;
using Chronos.Core.Domain.Map;

namespace Chronos.Client.Adapters;

public sealed class GodotCameraAdapter
{
    private readonly MapCamera _camera;

    public GodotCameraAdapter(MapCamera camera) => _camera = camera;

    /// Bridge: Godot Vector2 → pure domain int coordinates
    public void SetTargetFromWorldPos(Vector2 worldPos) =>
        _camera.SetTarget((int)worldPos.X, (int)worldPos.Y);

    /// Bridge: pure domain → Godot Vector2 for Node2D.Position
    public Vector2 ToGodotScreenOffset() =>
        new(_camera.PositionX, _camera.PositionY);
}
```

**Step 5 — Update ChronosCore.csproj and verify**

```bash
dotnet build ChronosCore/ChronosCore.csproj
# Must produce 0 errors, 0 warnings (TreatWarningsAsErrors=true)
```

### Priority Migration Order

| Priority | Class | Effort | Value |
|---|---|---|---|
| 1 | `AnimationController` | Low | High — pure state machine, testable |
| 2 | `LoginScreen` auth logic | Medium | High — extract to `LoginService` |
| 3 | `ButtonManager` config/state | Low | Medium — extract `ButtonState` model |
| 4 | `PanelManager` layout logic | Medium | Medium — extract `PanelLayout` |
| 5 | `ScreenManager` stack logic | Low | Medium — already near-pure |

`MapCamera`, `TileMapData`, `MapAnimClock`, `BackgroundItemGrid`, `CharacterPart`, `PacketCrypto`, `Protocol`, `BinEquipLoader`, `MapAssetPaths` are already pure or near-pure — enforce the boundary, do not add engine imports.

---

## 17. FILE & NAMESPACE LAYOUT

```
ChronosCore/
├── ChronosCore.csproj                      ← no Godot reference
├── Contracts/
│   ├── ILogger.cs
│   ├── ITimeSource.cs
│   ├── IFileSystem.cs
│   ├── IAssetLoader.cs
│   ├── IDrawContext.cs
│   ├── IInputSource.cs
│   └── TextureHandle.cs
├── Common/
│   └── Result.cs
├── Domain/
│   ├── Math/
│   │   ├── Vec2.cs
│   │   ├── Vec2I.cs
│   │   ├── Rect.cs
│   │   └── ColorF.cs
│   ├── Map/
│   │   ├── TileMapData.cs
│   │   ├── MapCamera.cs
│   │   ├── MapAnimClock.cs
│   │   ├── MapAssetPaths.cs
│   │   ├── BackgroundItem.cs
│   │   └── BackgroundItemGrid.cs
│   ├── Character/
│   │   ├── CharacterPart.cs
│   │   ├── CharacterState.cs
│   │   └── EquipmentRegistry.cs
│   ├── Animation/
│   │   └── AnimationStateMachine.cs
│   └── Npc/
│       └── NpcBrain.cs                     ← future
├── Application/
│   ├── MapLoadService.cs
│   ├── PlayerInputService.cs
│   └── GameSessionService.cs               ← future
└── Infrastructure/
    ├── Protocol/
    │   ├── PacketWriter.cs
    │   ├── SpanPacketReader.cs
    │   └── WireProtocol.cs
    ├── Equipment/
    │   └── EquipmentLoader.cs
    ├── Security/
    │   ├── PacketCrypto.cs
    │   └── CryptoUtils.cs
    └── Config/
        └── ClientConfig.cs

ChronosClient/                              ← Godot project
├── ChronosClient.csproj                    ← references ChronosCore
├── Adapters/
│   ├── GodotLogger.cs
│   ├── GodotTimeSource.cs
│   ├── GodotFileSystem.cs
│   ├── GodotAssetLoader.cs
│   ├── GodotDrawContext.cs
│   └── GodotInputSource.cs
└── Presentation/
    ├── Main.cs
    ├── LoginScreen.cs
    ├── MapRenderer.cs
    └── ...

ChronosCore.Tests/
├── ChronosCore.Tests.csproj
├── Doubles/
│   ├── FakeTimeSource.cs
│   ├── MemoryFileSystem.cs
│   └── SpyLogger.cs
└── Domain/
    ├── TileMapDataTests.cs
    ├── AnimationStateMachineTests.cs
    └── MapCameraTests.cs
```

### Namespace Convention

```
Chronos.Core.Contracts       ← interfaces only
Chronos.Core.Common          ← shared primitives (Result, etc.)
Chronos.Core.Domain.*        ← pure domain models and logic
Chronos.Core.Application.*   ← pure services orchestrating domain
Chronos.Core.Infrastructure.*← pure I/O adapters (no engine)
Chronos.Client.Adapters.*    ← Godot implementations of Core contracts
Chronos.Client.Presentation.*← Godot Nodes
```

---

*This document is the binding contract for engine-independent development in the Chronos project.  
Any code that crosses the boundary defined in §1 without going through an adapter in §3–4 is an architecture violation and must be rejected in code review.*
