namespace Daleks;

public readonly struct Vector2di
{
    public int X { get; }
    public int Y { get; }
    public int NormSqr => X * X + Y * Y;
    public double Norm => Math.Sqrt(NormSqr);

    public Vector2di(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return $"X={X}, Y={Y}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Vector2di v)
        {
            return false;
        }

        return v.X == this.X && v.Y == this.Y;
    }

    public Direction DirectionTo(Vector2di b)
    {
        if (this == b)
        {
            throw new ArgumentException("Cannot get direction to same point", nameof(b));
        }

        var a = this;
        
        return Enum.GetValues<Direction>().MinBy(n => DistanceSqr(a + n, b));
    }

    public static Vector2di operator +(Vector2di a, Vector2di b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2di operator +(Vector2di a, Direction d) => a + d.Offset();
    public static Vector2di operator -(Vector2di a, Vector2di b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2di operator /(Vector2di a, Vector2di b) => new(a.X / b.X, a.Y / b.Y);
    public static Vector2di operator /(Vector2di a, int s) => new(a.X / s, a.Y / s);
    public static Vector2di operator *(Vector2di a, Vector2di b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2di operator *(Vector2di a, int s) => new(a.X * s, a.Y * s);
    public static bool operator ==(Vector2di a, Vector2di b) => a.Equals(b);
    public static bool operator !=(Vector2di a, Vector2di b) => !a.Equals(b);

    public static readonly Vector2di Zero = new(0, 0);
    public static readonly Vector2di UnitX = new(1, 0);
    public static readonly Vector2di UnitY = new(0, 1);
    public static readonly Vector2di One = new(1, 1);

    public static int DistanceSqr(Vector2di a, Vector2di b) => (a - b).NormSqr;
    public static double Distance(Vector2di a, Vector2di b) => (a - b).Norm;
}