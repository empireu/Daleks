using Common;
using Daleks;

Console.Write("ID: ");
var id = int.Parse(Console.ReadLine()!);

const int rounds = 150;

var manager = new GameManager(id, rounds);

Controller? controller = null;
Vector2di? basePos = null;

while (true)
{
    Console.WriteLine($"----- Round {manager.Round} -----");

    var state = manager.Read();

    if (controller == null)
    {
        var match = manager.MatchInfo;
        Console.WriteLine($"Initializing game with grid of {match.GridSize}");
        controller = new Controller(match.GridSize, match.BasePosition, rounds);
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