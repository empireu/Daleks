using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Common;

void Log(string text) => Console.WriteLine(text);

var serverPort = Util.PromptInt("Server Port (3141): ", 3141);
var serverListener = new TcpListener(IPAddress.Any, serverPort);
serverListener.Start();

var clientPort = Util.PromptInt("Client Port (31415): ", 31415);

Log("Waiting for server...");

using var server = serverListener.AcceptTcpClient();
Log("Got server!");
serverListener.Stop();

Log("Waiting for client count...");
var count = server.ReceiveInt();
server.SendInt(1);

Log($"Waiting for {count} clients...");

var clientListener = new TcpListener(IPAddress.Any, clientPort);
clientListener.Start();

var clients = new HashBiMap<TcpClient, int>();

for (var i = 0; i < count; i++)
{
    Log($"Waiting for {i}th player...");
    var client = clientListener.AcceptTcpClient();
    Log("Waiting for auth...");

    var id = client.ReceiveInt();

    if (clients.ContainsBackward(id))
    {
        client.SendInt(0);
        throw new Exception($"Duplicate ID {clients}");
    }

    client.SendInt(1);

    clients.Associate(client, id);

    Log($"{i}th logged in as {id}");
}

Log("Starting game!");

while (true)
{
    void Fail(object msg) => Trace.Fail($"Server crashed: {msg}");

    var round = server.ReceiveInt();

    Log($"----- Round {round} -----\n\n");

    var targetId = server.ReceiveInt();
    var serverData = server.ReceiveString();

    Log($"Received for {targetId}");
    var client = clients.Backward[targetId];

    client.SendString(serverData);

    Log("Receiving commands...");

    string clientData;
    try
    {
        clientData = client.ReceiveString();
    }
    catch (Exception e)
    {
        Fail($"Failed to collect response from {targetId}: {e}");
        throw;
    }

    Log("Relaying...");

    server.SendString(clientData);

    Console.WriteLine("\n\n");
}