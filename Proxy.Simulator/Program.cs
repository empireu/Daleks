using System.Diagnostics;
using System.Net.Sockets;
using Common;
using Proxy.Simulator;

var count = Util.PromptInt("Lobby size: ");

if (count is < 0 or > 5)
{
    throw new Exception($"Invalid lobby size {count}");
}

var port = Util.PromptInt("Server port (3141): ", 3141);
var host = Util.Prompt("Host: ");

Util.LogInfo($"Connecting to \"{host}\":{port}");

using var connection = new TcpClient(host, port);

Util.LogInfo("Performing handshake...");

connection.SendInt(count);

if (connection.ReceiveInt() != 1)
{
    throw new Exception("Handshake failed");
}

Util.LogInfo("Starting game!");

var reader = new ServerReader(count);

var r = 0;

while (true)
{
    reader.GetNext(out var serverData, out var playerId, out var round);

    Util.LogInfo($"Found {playerId} @ {round}");

    connection.SendInt(round);
    connection.SendInt(playerId);
    connection.SendString(serverData);

    Util.LogInfo("Receiving commands");
    var clientData = connection.ReceiveString();

    Util.LogInfo("Writing");
    File.WriteAllText($"./game/c{playerId}_{round}.txt", clientData);

    Console.WriteLine("\n");

    if (r != round)
    {
        r = round;
        Util.LogWarn($"Alive: {string.Join(", ", reader.Alive)}");
        Console.WriteLine("-----------------");
    }
}