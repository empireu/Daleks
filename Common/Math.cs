using System.Numerics;
using System.Runtime.Intrinsics.X86;

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
    public short X { get; init; }
    public short Y { get; init; }
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

    public static Vector2d BarycenterMany(IEnumerable<Vector2di> points)
    {
        var x = 0d;
        var y = 0d;

        var i = 0;

        foreach (var p in points)
        {
            x += (p.X - x) / (i + 1);
            y += (p.Y - y) / (i + 1);
            i++;
        }

        return new Vector2d(x, y);
    }
}

public readonly struct Vector2d
{
    public static readonly Vector2d Zero = new(0, 0);
    public static readonly Vector2d One = new(1, 1);
    public static readonly Vector2d UnitX = new(1, 0);
    public static readonly Vector2d UnitY = new(0, 1);

    public double X { get; }
    public double Y { get; }

    public Vector2d(double x, double y)
    {
        X = x;
        Y = y;
    }

    public Vector2d(double value) : this(value, value)
    {

    }

    // We may consider using functions here. Normalized() is already using it.
    public double NormSqr => X * X + Y * Y;
    public double Norm => Math.Sqrt(NormSqr);
    public Vector2d Normalized() => this / Norm;

    public override bool Equals(object? obj)
    {
        if (obj is not Vector2d other)
        {
            return false;
        }

        return Equals(other);
    }

    public bool Equals(Vector2d other) => X.Equals(other.X) && Y.Equals(other.Y);

    public bool ApproxEq(Vector2d other, double eps = 1e-7) => X.ApproxEq(other.X, eps) && Y.ApproxEq(other.Y, eps);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"X={X}, Y={Y}";

    public Vector2di Floor() => new((int)Math.Floor(X), (int)Math.Floor(Y));
    public Vector2di Round() => new((int)Math.Round(X), (int)Math.Round(Y));
    public Vector2di Ceiling() => new((int)Math.Ceiling(X), (int)Math.Ceiling(Y));

    public static bool operator ==(Vector2d a, Vector2d b) => a.Equals(b);
    public static bool operator !=(Vector2d a, Vector2d b) => !a.Equals(b);
    public static Vector2d operator +(Vector2d v) => new(+v.X, +v.Y);
    public static Vector2d operator -(Vector2d v) => new(-v.X, -v.Y);
    public static Vector2d operator +(Vector2d a, Vector2d b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2d operator -(Vector2d a, Vector2d b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2d operator *(Vector2d a, Vector2d b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2d operator /(Vector2d a, Vector2d b) => new(a.X / b.X, a.Y / b.Y);
    public static Vector2d operator *(Vector2d v, double scalar) => new(v.X * scalar, v.Y * scalar);
    public static Vector2d operator /(Vector2d v, double scalar) => new(v.X / scalar, v.Y / scalar);
    public static implicit operator Vector2(Vector2d v) => new((float)v.X, (float)v.Y);
    public static implicit operator Vector2d(Vector2 v) => new(v.X, v.Y);
}

public readonly struct Rotation2d
{
    public static readonly Rotation2d Zero = Exp(0);

    public float Re { get; init; }
    public float Im { get; init; }

    public Rotation2d(float re, float im)
    {
        Re = re;
        Im = im;
    }

    public static Rotation2d Exp(float angleIncr) => new(MathF.Cos(angleIncr), MathF.Sin(angleIncr));

    public static Rotation2d Dir(Vector2 direction)
    {
        if (!direction.LengthSquared().ApproxEq(1))
        {
            direction = Vector2.Normalize(direction);
        }

        return new Rotation2d(direction.X, direction.Y);
    }

    public float Log() => MathF.Atan2(Im, Re);
    public Rotation2d Scaled(float k) => Exp(Log() * k);
    public Rotation2d Inverse => new(Re, -Im);
    public Vector2 Direction => new(Re, Im);

    public override bool Equals(object? obj)
    {
        if (obj is not Rotation2d other)
        {
            return false;
        }

        return Equals(other);
    }

    public bool Equals(Rotation2d other) => other.Re.Equals(Re) && other.Im.Equals(Im);

    public bool ApproxEq(Rotation2d other, float eps = 1e-7f) => Re.ApproxEq(other.Re, eps) && Im.ApproxEq(other.Im, eps);

    public override int GetHashCode() => HashCode.Combine(Re, Im);

    public override string ToString() => $"{Log():F4} rad";

    public static bool operator ==(Rotation2d a, Rotation2d b) => a.Equals(b);
    public static bool operator !=(Rotation2d a, Rotation2d b) => !a.Equals(b);
    public static Rotation2d operator *(Rotation2d a, Rotation2d b) => new(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);
    public static Vector2 operator *(Rotation2d a, Vector2 r2) => new(a.Re * r2.X - a.Im * r2.Y, a.Im * r2.X + a.Re * r2.Y);
    public static Rotation2d operator /(Rotation2d a, Rotation2d b) => b.Inverse * a;

    public static Rotation2d Interpolate(Rotation2d r0, Rotation2d r1, float t)
    {
        return Exp(t * (r1 / r0).Log()) * r0;
    }
}

public readonly struct Twist2d
{
    public static readonly Twist2d Zero = new(0, 0, 0);

    public Vector2 TransVel { get; init; }

    public float RotVel { get; init; }

    public Twist2d(Vector2 transVel, float rotVel)
    {
        TransVel = transVel;
        RotVel = rotVel;
    }

    public Twist2d(float vx, float vy, float vr)
    {
        TransVel = new Vector2(vx, vy);
        RotVel = vr;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Twist2d other)
        {
            return false;
        }

        return Equals(other);
    }

    public bool Equals(Twist2d other) => TransVel.Equals(other.TransVel) && RotVel.Equals(other.RotVel);

    public override int GetHashCode() => HashCode.Combine(TransVel, RotVel);

    public override string ToString() => $"{TransVel} {RotVel}";

    public static bool operator ==(Twist2d a, Twist2d b) => a.Equals(b);
    public static bool operator !=(Twist2d a, Twist2d b) => !a.Equals(b);
    public static Twist2d operator +(Twist2d a, Twist2d b) => new(a.TransVel + b.TransVel, a.RotVel + b.RotVel);
    public static Twist2d operator -(Twist2d a, Twist2d b) => new(a.TransVel - b.TransVel, a.RotVel - b.RotVel);
    public static Twist2d operator *(Twist2d tw, float scalar) => new(tw.TransVel * scalar, tw.RotVel * scalar);
    public static Twist2d operator /(Twist2d tw, float scalar) => new(tw.TransVel / scalar, tw.RotVel / scalar);
}

public readonly struct Pose2d
{
    public Vector2 Translation { get; init; }
    public Rotation2d Rotation { get; init; }

    public Pose2d(Vector2 translation, Rotation2d rotation)
    {
        Translation = translation;
        Rotation = rotation;
    }

    public Pose2d(float x, float y, float r)
    {
        Translation = new Vector2(x, y);
        Rotation = Rotation2d.Exp(r);
    }

    public Pose2d Inverse => new(Rotation.Inverse * -Translation, Rotation.Inverse);

    public override bool Equals(object? obj)
    {
        if (obj is not Pose2d other)
        {
            return false;
        }

        return Equals(other);
    }

    public bool Equals(Pose2d other) => Translation.Equals(other.Translation) && Rotation.Equals(other.Rotation);

    public override int GetHashCode() => HashCode.Combine(Translation, Rotation);

    public override string ToString() => $"{Translation} {Rotation}";

    public static bool operator ==(Pose2d a, Pose2d b) => a.Equals(b);
    public static bool operator !=(Pose2d a, Pose2d b) => !a.Equals(b);
    public static Pose2d operator *(Pose2d a, Pose2d b) => new(a.Translation + a.Rotation * b.Translation, a.Rotation * b.Rotation);
    public static Vector2 operator *(Pose2d a, Vector2 v) => a.Translation + a.Rotation * v;
    public static Pose2d operator /(Pose2d a, Pose2d b) => b.Inverse * a;
}