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
