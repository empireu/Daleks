using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Common;

namespace MapGenerator;

public static class Simulacru
{
    private const string DllFile = "SimulacrSUS.dll";

    static Simulacru()
    {
        if (!File.Exists(DllFile))
        {
            var stream = typeof(Simulacru).Assembly.GetManifestResourceStream($"MapGenerator.{DllFile}");

            if (stream == null)
            {
                Trace.Fail("Failed to acquire simulacru");
            }

            using var fs = File.Create(DllFile);

            stream!.CopyTo(fs);
        }
    }

    [DllImport("SimulacrSUS.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern unsafe void GenerateWorldApi(
        int mazeWidth, 
        int mazeHeight, 
        int seed, 
        bool fewerResources, 
        byte[] lpTarget, 
        out int mapWidth,
        out int mapHeight
    );

    [DllImport("SimulacrSUS.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int TimeSeed();

    private static Vector2ds MazeSize(Vector2ds mapSize) => new((mapSize.X + 1) / 2, (mapSize.Y + 1) / 2);

    public static unsafe Grid<TileType> Generate(Vector2ds mapSize, int seed, bool fewerResources)
    {
        var mazeSize = MazeSize(mapSize);

        var buffer = new byte[mapSize.X * mapSize.Y];

        GenerateWorldApi(
            mazeSize.X,
            mazeSize.Y, 
            seed, 
            fewerResources, 
            buffer, 
            out var genW, 
            out var genH
        );

        if (genW != mapSize.X || genH != mapSize.Y)
        {
            throw new Exception($"Invalid map size {mapSize}");
        }

        var grid = new Grid<TileType>(mapSize);

        for (var y = 0; y < mapSize.Y; y++)
        {
            for (var x = 0; x < mapSize.X; x++)
            {
                grid[x, y] = Tiles.ParseTile((char)buffer[y * mapSize.X + x]);
            }
        }

        return grid;
    }
}