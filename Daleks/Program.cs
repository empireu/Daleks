using Common;
using Daleks;

Console.Write("ID: ");
var id = int.Parse(Console.ReadLine()!);
Console.Write("\nRounds (150): ");

int rounds = 150;

if (int.TryParse(Console.ReadLine(), out var i))
{
    rounds = i;
}

Console.WriteLine($"\nStarting player {id} @ {rounds} rounds");

var manager = new GameManager(id, rounds);

Bot? controller = null;

while (true)
{
    Console.WriteLine($"----- Round {manager.Round} -----");

    var state = manager.Poll();

    if (controller == null)
    {
        var match = manager.MatchInfo;
        Console.WriteLine($"Initializing game with grid of {match.GridSize}");
        controller = new Bot(match, new BotConfig(), rounds);
    }

    var player = state.Player;

    Console.WriteLine($"HP: {player.Hp}");


    Console.WriteLine("Abilities:\n" +
                      $"  dig: {player.Dig}\n" +
                      $"  attack: {player.Attack}\n" +
                      $"  movement: {player.Movement}\n" +
                      $"  vision: {player.Sight}\n" +
                      $"  antenna: {(player.HasAntenna ? "yes" : "no")}\n" +
                      $"  battery: {(player.HasBattery ? "yes" : "no")}");
    Console.WriteLine("Inventory:\n" +
                      $"  cobblestone: {player.CobbleCount}\n" +
                      $"  iron: {player.IronCount}\n" +
                      $"  osmium: {player.OsmiumCount}");

    var cl = new CommandState(state, manager.MatchInfo.BasePosition);
    
    controller.Update(cl);
    manager.Submit(cl);

    Console.WriteLine("------------------------\n\n\n");
}