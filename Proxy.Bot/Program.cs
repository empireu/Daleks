using System.Net.Sockets;
using Common;

if (Directory.Exists("game"))
{
    Directory.Delete("game", true);
}

Directory.CreateDirectory("game");

var host = Util.Prompt("Host: ");
var port = Util.PromptInt("Port (31415): ", 31415);
var id = Util.PromptInt("ID: ");

if (id is < 0 or > 5)
{
    throw new Exception("Invalid ID");
}

Console.WriteLine("Connecting...");

using var client = new TcpClient(host, port);

client.SendInt(id);

if (client.ReceiveInt() != 1)
{
    throw new Exception("Server rejected!");
}

Console.WriteLine("Starting game...\n\n");

var round = 0;

while (true)
{
    Console.WriteLine($"---- Round {round} ----");
    Console.WriteLine("Waiting for server...");

    var serverData = client.GetStream().ReadString();

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

    client.SendString(clientData);

    Console.WriteLine("Done!\n\n");

    round++;
}