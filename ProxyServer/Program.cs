using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ProxyNetworking;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Verbose)
    .Enrich.FromLogContext()
    .WriteTo.File($"logs/logs-{DateTime.Now.ToString("s").Replace(":", ".")}.txt", LogEventLevel.Debug, rollingInterval: RollingInterval.Infinite)
    .WriteTo.Console()
    .CreateLogger();


var lobbySize = Util.PromptInt("Lobby size: ");

if (lobbySize is < 0 or > 5)
{
    throw new Exception($"Invalid lobby size {lobbySize}");
}

var port = Util.PromptInt("Port: ");

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

var ids = new Dictionary<TcpClient, int>();

for (var i = 0; i < lobbySize; i++)
{
    Log.Information("Waiting for {i}th player...", i);
    var client = listener.AcceptTcpClient();

    Log.Information("Waiting for auth...");
    var id = await client.GetStream().ReadIntAsync();

    if (ids.Values.Contains(id))
    {
        await client.GetStream().WriteIntAsync(0);
        client.Dispose();
        throw new Exception($"Duplicate ID {ids}");
    }

    await client.GetStream().WriteIntAsync(1);

    ids.Add(client, id);

    Log.Information("{i}th logged in as {id}", i, id);
}

Log.Information("Starting game!");

var round = 0;


while (true)
{
    void Fail() => Trace.Fail("Server crashed");

    Log.Information("Round {r}", round);
    
    await Task.WhenAll(ids.Keys.Select(async client =>
    {
        var id = ids[client];

        string serverData;

        while (true)
        {
            try
            {
                serverData = await File.ReadAllTextAsync($"./game/s{id}_{round}.txt");
                break;
            }
            catch (IOException)
            {
                await Task.Delay(25);
            }
            catch (Exception e)
            {
                Log.Fatal("Failed to read simulator {i}: {e}", id, e);
                Fail();
                return;
            }
        }

        try
        {
            await client.GetStream().WriteStringAsync(serverData);
        }
        catch (Exception e)
        {
            Log.Error("Failed to send to {i}: {e}", id, e);
            Fail();
        }

        string clientData;

        try
        {
            Log.Information("Waiting for {id}", id);
            clientData = await client.GetStream().ReadStringAsync();
        }
        catch (Exception e)
        {
            Log.Fatal("Failed to read client response {i}: {e}", id, e);
            Fail();
            return;
        }

        try
        {
            await File.WriteAllTextAsync($"./game/c{id}_{round}.txt", clientData);
        }
        catch (Exception e)
        {
            Log.Fatal("Failed to write client response {i}: {e}", id, e);
            Fail();
        }

        Log.Information("Finished {id}", id);
    }));

    Console.WriteLine("\n\n");

    round++;
}

