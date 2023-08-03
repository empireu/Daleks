using System.Net.Sockets;
using ProxyNetworking;

if (Directory.Exists("game"))
{
    Directory.Delete("game", true);
}

Directory.CreateDirectory("game");

var host = Util.Prompt("Host: ");
var port = Util.PromptInt("Port: ");
var id = Util.PromptInt("ID: ");

if (id is < 0 or > 5)
{
    throw new Exception("Invalid ID");
}

Console.WriteLine("Connecting...");

using var client = new TcpClient(host, port);

await client.GetStream().WriteIntAsync(id);

var accepted = (await client.GetStream().ReadIntAsync()) == 1;

if (!accepted)
{
    throw new Exception("Server rejected!");
}

Console.WriteLine("Starting game...\n\n");

var round = 0;

while (true)
{
    Console.WriteLine($"---- Round {round} ----");
    Console.WriteLine("Waiting for server...");

    var serverData = await client.GetStream().ReadStringAsync();

    Console.WriteLine($"Read {serverData.Length}");

    File.WriteAllText($"./game/s{id}_{round}.txt", serverData);

    Console.WriteLine("Polling...");

    string clientData;

    while (true)
    {
        try
        {
            clientData = File.ReadAllText($"./game/c{id}_{round}.txt");
            break;
        }
        catch (IOException)
        {
            Thread.Sleep(10);
        }
    }

    await client.GetStream().WriteStringAsync(clientData);

    Console.WriteLine("Done!\n\n");

    round++;
}
