using System.Runtime.CompilerServices;

namespace Common;

public enum Direction : byte
{
    U = 0,
    D = 1,
    L = 2,
    R = 3
}

public readonly struct Vector2di : IComparable<Vector2di>
{
    public short X { get; }
    public short Y { get; }
    public int NormSqr => X * X + Y * Y;
    public double Norm => Math.Sqrt(NormSqr);
    public float NormF => MathF.Sqrt(NormSqr);

    private static readonly Direction[] Directions = Enum.GetValues<Direction>(); 

    public Vector2di(short x, short y)
    {
        X = x;
        Y = y;
    }

    public Vector2di(int x, int y)
    {
        X = (short)x;
        Y = (short)y;
    }

    public override string ToString() => $"X={X}, Y={Y}";

    public override bool Equals(object? obj) => obj is Vector2di v && this.Equals(v);

    public bool Equals(Vector2di other) => X == other.X && Y == other.Y;

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public Direction DirectionTo(Vector2di b)
    {
        if (this == b)
        {
            throw new ArgumentException("Cannot get direction to same point", nameof(b));
        }

        var a = this;

        return Directions.MinBy(n => DistanceSqr(a + n, b));
    }

    public static Vector2di operator +(Vector2di a, Vector2di b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2di operator +(Vector2di a, Direction d) => a + d.Step();
    public static Vector2di operator -(Vector2di a, Vector2di b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2di operator -(Vector2di a, Direction d) => a - d.Step();
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
    public static float DistanceF(Vector2di a, Vector2di b) => (a - b).NormF;
    public static int Manhattan(Vector2di a, Vector2di b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    public int CompareTo(Vector2di other) => this.NormSqr.CompareTo(other.NormSqr);
}