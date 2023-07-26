using Daleks;
using System.IO;

Console.Write("Id: ");
var id = int.Parse(Console.ReadLine()!);

var round = 0;

GameState ReadState()
{
    Console.WriteLine("Polling...");

    var path = $"./game/s{id}_{round}.txt";

    while(!File.Exists(path))
    {
        Thread.Sleep(500);
    }

    Console.WriteLine($"Read {round}");

    return GameState.Parse(File.ReadAllLines(path));
}

void Submit(CommandList cl)
{
    var str = cl.Serialize();
    File.WriteAllText($"./game/c{id}_{round}.txt", str);
    
    Console.WriteLine("Submitting...");

    Console.WriteLine($"Moves: {string.Join(' ', cl.Moves)}");

    if (cl.Action.HasValue)
    {
        Console.WriteLine($"Doing {cl.Action.Value.Type} towards {cl.Action.Value.Dir}");
    }

    Console.WriteLine($"Buying {string.Join(", ", cl.Upgrades)}");
}

while (true)
{
    Console.WriteLine($"----- Round {round} -----");

    var state = ReadState();

    Console.WriteLine($"HP: {state.HP}");

    Console.WriteLine("Abilities:\n" +
                      $"  dig: {state.Dig}\n" +
                      $"  attack: {state.Attack}\n" +
                      $"  movement: {state.Movement}\n" +
                      $"  vision: {state.Vision}\n" +
                      $"  antenna: {(state.HasAntenna ? "yes" : "no")}\n" +
                      $"  battery: {(state.HasBattery ? "yes" : "no")}");

    Console.WriteLine("Inventory:\n" +
                      $"  cobblestone: {state.CobbleCount}\n" +
                      $"  iron: {state.IronCount}\n" +
                      $"  osmium: {state.OsmiumCount}");

    var cl = new CommandList();

    cl.Moves.Add(Direction.U);

    Submit(cl);

    round++;
}