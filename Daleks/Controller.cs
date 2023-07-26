using System.Data;

namespace Daleks;

internal class Controller
{
    public void Update(CommandList cl)
    {
        Console.WriteLine($"Pos: {cl.Tail.Player.ActualPos}");

        var state = cl.Tail;

        if (state.Neighbor(Direction.U) is TileType.Dirt or TileType.Base)
        {
            cl.Move(Direction.U);
            return;
        }
        else
        {
            Console.WriteLine($"Obstacle: {state.Neighbor(Direction.U)}");
        }

        cl.Mine(new [] { Direction.U });
    }
}