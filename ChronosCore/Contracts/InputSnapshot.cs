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
