using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.IO;

namespace ProxyNetworking;

public static class StreamExtensions
{
    public static async ValueTask ReadManyAsync(this Stream stream, Memory<byte> destination)
    {
        if (destination.Length == 0)
        {
            return;
        }

        while (destination.Length > 0)
        {
            var read = await stream.ReadAsync(destination);

            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            destination = destination[read..];
        }
    }

    public static async ValueTask<int> ReadIntAsync(this Stream stream)
    {
        var buffer = new byte[4];
        await ReadManyAsync(stream, buffer);
        return BitConverter.ToInt32(buffer);
    }

    public static async ValueTask WriteIntAsync(this Stream stream, int value)
    {
        var buffer = BitConverter.GetBytes(value);
        await stream.WriteAsync(buffer);
    }

    public static async ValueTask<byte[]> ReadBytesAsync(this Stream stream)
    {
        var size = await stream.ReadIntAsync();
        var result = new byte[size];

        await stream.ReadManyAsync(result);

        return result;
    }

    public static async ValueTask WriteBytesAsync(this Stream stream, Memory<byte> bytes)
    {
        var sizeBuffer = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(sizeBuffer);
        await stream.WriteAsync(bytes);
    }

    public static async ValueTask<string> ReadStringAsync(this Stream stream)
    {
        return Encoding.UTF8.GetString(await stream.ReadBytesAsync());
    }

    public static async ValueTask WriteStringAsync(this Stream stream, string str)
    {
        await stream.WriteBytesAsync(Encoding.UTF8.GetBytes(str));
    }
}