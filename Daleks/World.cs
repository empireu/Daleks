using System.Drawing;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace Daleks;

public class GameState
{
    public Size GridSize { get; }

    public TileType[] Grid { get; }

    public GameState(Size gridSize)
    {
        GridSize = gridSize;
        Grid = new TileType[gridSize.Width * gridSize.Height];
    }

    public Point ActualPos;
    public int HP;
    public int Dig;
    public int Attack;
    public int Movement;
    public int Vision;
    public bool HasAntenna;
    public bool HasBattery;
    public int CobbleCount;
    public int IronCount;
    public int OsmiumCount;

    public ref TileType CellAt(int x, int y) => ref Grid[GetIndex(x, y)];
    public ref TileType this[int x, int y] => ref CellAt(x, y);
    public int GetIndex(int x, int y) => y * GridSize.Width + x;

    private static Size PopSize(ref Span<string> lines)
    {
        var result = lines[0].Map(str =>
        {
            var tokens = str.Split(' ');

            return new Size(int.Parse(tokens[0]), int.Parse(tokens[1]));
        });

        lines = lines[1..];

        return result;
    }

    public static GameState Parse(Span<string> lines)
    {
        var size = PopSize(ref lines);
        var state = new GameState(size);

        for (var y = 0; y < size.Height; y++)
        {
            var line = lines[y].Replace(" ", "");

            for (var x = 0; x < size.Width; x++)
            {
                var c = line[x];

                state[x, y] = c switch
                {
                    '.' => TileType.Dirt,
                    'X' => TileType.Stone,
                    'A' => TileType.Cobblestone,
                    'B' => TileType.Bedrock,
                    'C' => TileType.Iron,
                    'D' => TileType.Osmium,
                    'E' => TileType.Base,
                    'F' => TileType.Acid,
                    '?' => TileType.Unknown,
                    _ => char.IsDigit(c) ? TileType.Robot : throw new Exception($"Invalid tile {c}")
                };
            }
        }

        lines = lines[size.Height..];

        PopSize(ref lines).Also(p => state.ActualPos = new Point(p.Width, p.Height));

        lines[0].Split(' ').Also(tokens =>
        {
            state.HP = int.Parse(tokens[0]);
            state.Dig = int.Parse(tokens[1]);
            state.Attack = int.Parse(tokens[2]);
            state.Movement = int.Parse(tokens[3]);
            state.Vision = int.Parse(tokens[4]);
            state.HasAntenna = int.Parse(tokens[5]) == 1;
            state.HasBattery = int.Parse(tokens[6]) == 1;
        });

        lines = lines[1..];

        lines[0].Split(' ').Also(tokens =>
        {
            state.CobbleCount = int.Parse(tokens[0]);
            state.IronCount = int.Parse(tokens[1]);
            state.OsmiumCount = int.Parse(tokens[2]);
        });

        return state;
    }
}

public enum TileType
{
    Dirt,
    Stone,
    Cobblestone,
    Bedrock,
    Iron,
    Osmium,
    Base,
    Acid,
    Unknown,
    Robot
}