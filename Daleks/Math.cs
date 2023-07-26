namespace Daleks;

public readonly struct Vector2di
{
    public int X { get; }
    public int Y { get; }

    public Vector2di(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return $"X={X}, Y={Y}";
    }

    public static Vector2di operator +(Vector2di a, Vector2di b) => new(a.X + b.X, a.Y + b.Y);

    public static readonly Vector2di Zero = new(0, 0);
    public static readonly Vector2di UnitX = new(1, 0);
    public static readonly Vector2di UnitY = new(0, 1);
    public static readonly Vector2di One = new(1, 1);
}