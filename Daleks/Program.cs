using Daleks;

Console.Write("Id: ");
var id = int.Parse(Console.ReadLine()!);

Console.Write("\nRounds (500): ");
var rounds = int.TryParse(Console.ReadLine(), out var i) ? i : 500;

var round = 0;

GameState ReadState()
{
    Console.WriteLine("Polling...");

    var path = $"./game/s{id}_{round}.txt";

    while (true)
    {
        try
        {
            var state = GameState.Load(File.ReadAllLines(path), round);
            Console.WriteLine($"Read {round}");
            return state;
        }
        catch (IOException)
        {
            Thread.Sleep(25);
        }
    }
}

void Submit(CommandState cl)
{
    var str = cl.Serialize();
    File.WriteAllText($"./game/c{id}_{round}.txt", str);
    
    Console.WriteLine("Submitting...");

    Console.WriteLine($"Moves: {string.Join(' ', cl.Moves)}");
    if (cl.Moves.Count == 0)
    {
        Console.WriteLine("  N/A");
    }

    if (cl.HasAction)
    {
        Console.WriteLine($"Doing:");
      
        foreach (var actionCommand in cl.Actions)
        {
            Console.WriteLine($"  {actionCommand.Type} towards {actionCommand.Dir}");
        }
    }

    if (cl.Upgrades.Count > 0)
    {
        Console.WriteLine($"Buying {string.Join(", ", cl.Upgrades)}");
    }
}

Controller? controller = null;

while (true)
{
    Console.WriteLine($"----- Round {round} -----");

    var state = ReadState();

    if (controller == null)
    {
        Console.WriteLine($"Initializing game with grid of {state.GridSize}");
        controller = new Controller(state.GridSize, state.Player.ActualPos, rounds);
    }

    var player = state.Player;

    Console.WriteLine($"HP: {player.Hp}");


    Console.WriteLine("Abilities:\n" +
                      $"  dig: {player.Dig}\n" +
                      $"  attack: {player.Attack}\n" +
                      $"  movement: {player.Movement}\n" +
                      $"  vision: {player.Vision}\n" +
                      $"  antenna: {(player.HasAntenna ? "yes" : "no")}\n" +
                      $"  battery: {(player.HasBattery ? "yes" : "no")}");
    Console.WriteLine("Inventory:\n" +
                      $"  cobblestone: {player.CobbleCount}\n" +
                      $"  iron: {player.IronCount}\n" +
                      $"  osmium: {player.OsmiumCount}");

    var cl = new CommandState(state);
    controller.Update(cl);
    Submit(cl);

    round++;

    Console.WriteLine("------------------------\n\n\n");
}