using System.Reflection.Metadata.Ecma335;
using Common;

namespace Proxy.Simulator;

internal class ServerReader
{
    private readonly HashSet<int> _read = new();
    private readonly HashSet<int> _alive;

    public int Round { get; private set; }

    private IEnumerable<int> Unread => _alive.Where(a => !_read.Contains(a)).OrderBy(i => i);
    
    public IEnumerable<int> Alive => _alive.OrderBy(i => i);

    public ServerReader(int lobbySize)
    {
        _alive = Enumerable.Range(0, lobbySize).ToHashSet();
    }

    private void MoveNext()
    {
        _read.Clear();
        Round++;
    }

    public void GetNext(out string serverData, out int playerId, out int round)
    {
        if (!Unread.Any())
        {
            // All players are handled, go to next round
            MoveNext();
            GetNext(out serverData, out playerId, out round);
            return;
        }

        if (_alive.Count == 0)
        {
            throw new Exception("Ran out of players");
        }

        while (true)
        {
            // Checks if the server skipped to the next round:
            foreach (var id in Alive)
            {
                if (File.Exists(Path(Round + 1, id)))
                {
                    // The server moved to the next round. Players that we haven't read yet are not being handled by the server anymore,
                    // so we will mark them as dead.

                    foreach (var droppedId in Unread)
                    {
                        Util.LogWarn($"NEXT DROPPING {droppedId}");
                        _alive.Remove(droppedId);
                    }

                    MoveNext();
                    GetNext(out serverData, out playerId, out round);
                    return;
                }
            }

            int? found = null;
            serverData = "";

            foreach (var i in Unread)
            {
                try
                {
                    serverData = File.ReadAllText(Path(Round, i));
                    found = i;
                    break;
                }
                catch (IOException)
                {
                    Thread.Yield();
                }
            }

            if (found == null)
            {
                Thread.Sleep(10);
                continue;
            }

            playerId = found.Value;

            var unread = Unread.ToArray();
            Array.Sort(unread);

            // The server writes data in order, so clients with ID smaller than the one we read for will be dropped:
            foreach (var id in unread)
            {
                if (id == playerId)
                {
                    break;
                }

                Util.LogWarn($"SEQ DROPPING {id}");
                _alive.Remove(id);
            }

            _read.Add(playerId);

            round = Round;

            return;
        }
    }

    private static string Path(int round, int id) => $"./game/s{id}_{round}.txt";
}