using Daleks;

Console.Write("Id: ");
var id = int.Parse(Console.ReadLine()!);

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
        catch (IOException e)
        {
            Thread.Sleep(100);
        }
    }
}

void Submit(CommandList cl)
{
    var str = cl.Serialize();
    File.WriteAllText($"./game/c{id}_{round}.txt", str);
    
    Console.WriteLine("Submitting...");

    Console.WriteLine($"Moves: {string.Join(' ', cl.Moves)}");

    if (cl.HasAction)
    {
        Console.WriteLine($"Doing:");
      
        foreach (var actionCommand in cl.Actions)
        {
            Console.WriteLine($"  {actionCommand.Type} towards {actionCommand.Dir}");
        }
    }

    Console.WriteLine($"Buying {string.Join(", ", cl.Upgrades)}");
}

var controller = new Controller();

while (true)
{
    Console.WriteLine($"----- Round {round} -----");

    var state = ReadState();
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

    var cl = new CommandList(state);
    controller.Update(cl);
    Submit(cl);

    round++;

    Console.WriteLine("------------------------\n\n\n");
}