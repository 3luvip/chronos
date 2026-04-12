namespace Chronos.Core.Domain;

public readonly record struct Vec2I(int X, int Y)
{
    public static readonly Vec2I Zero = new(0, 0);
    public static Vec2I operator +(Vec2I a, Vec2I b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2I operator -(Vec2I a, Vec2I b) => new(a.X - b.X, a.Y - b.Y);
    public Vec2 ToVec2() => new(X, Y);
}
