using System.Drawing;
using System.Runtime.CompilerServices;

namespace Daleks;

public class Grid
{
    public Size Size { get; }

    public Tile[] Cells { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref Tile CellAt(int x, int y) => ref Cells[GetIndex(x, y)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetIndex(int x, int y) => y * Size.Width + x;
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
    Unknown
}

public readonly struct Tile
{
    public readonly TileType Type;
}