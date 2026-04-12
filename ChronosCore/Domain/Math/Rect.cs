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
