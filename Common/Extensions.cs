using System.Drawing;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Text;

namespace Common;

public static class Extensions
{
    public static T Also<T>(this T t, Action<T> action)
    {
        action(t);
        return t;
    }

    public static T2 Map<T1, T2>(this T1 t, Func<T1, T2> m)
    {
        return m(t);
    }

    public static Vector2di Step(this Direction dir) => dir switch
    {
        Direction.L => new Vector2di(-1, 0),
        Direction.R => new Vector2di(1, 0),
        Direction.U => new Vector2di(0, -1),
        Direction.D => new Vector2di(0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, $"Unexpected direction {dir}")
    };

    public static bool Contains(this Rectangle rect, Vector2di p) => rect.Contains(p.X, p.Y);
    public static Vector2di CenterI(this Rectangle rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    public static Vector2 Center(this Rectangle rect) => new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

    public static void ReadMany(this Stream stream, Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return;
        }

        while (destination.Length > 0)
        {
            var read = stream.Read(destination);

            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            destination = destination[read..];
        }
    }

    public static int ReadInt(this Stream stream)
    {
        var buffer = new byte[4];
        ReadMany(stream, buffer);
        return BitConverter.ToInt32(buffer);
    }

    public static void WriteInt(this Stream stream, int value)
    {
        var buffer = BitConverter.GetBytes(value);
        stream.Write(buffer);
    }

    public static byte[] ReadBytes(this Stream stream)
    {
        var size = stream.ReadInt();
        var result = new byte[size];

        stream.ReadMany(result);

        return result;
    }

    public static void WriteBytes(this Stream stream, ReadOnlySpan<byte> bytes)
    {
        var sizeBuffer = BitConverter.GetBytes(bytes.Length);
        stream.Write(sizeBuffer);
        stream.Write(bytes);
    }

    public static string ReadString(this Stream stream)
    {
        return Encoding.UTF8.GetString(stream.ReadBytes());
    }

    public static void WriteString(this Stream stream, string str)
    {
        stream.WriteBytes(Encoding.UTF8.GetBytes(str));
    }

    public static void SendString(this TcpClient client, string str)
    {
        client.GetStream().WriteString(str);
    }

    public static void SendInt(this TcpClient client, int i)
    {
        client.GetStream().WriteInt(i);
    }

    public static string ReceiveString(this TcpClient client)
    {
        return client.GetStream().ReadString();
    }

    public static int ReceiveInt(this TcpClient client)
    {
        return client.GetStream().ReadInt();
    }
    
    public static bool ApproxEq(this float f, float other, float threshold = 1e-7f)
    {
        return Math.Abs(f - other) < threshold;
    }

    public static bool ApproxEq(this double d, double other, double threshold = 1e-7)
    {
        return Math.Abs(d - other) < threshold;
    }
}