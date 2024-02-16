using Common;

namespace Daleks;

public sealed class MatchInfo
{
    public Vector2ds BasePosition { get; init; }
    public Vector2ds GridSize { get; init; }
}

public class GameManager
{
    public int Id { get; }
    public int AcidRounds { get; }
    public int Round { get; private set; }

    private MatchInfo? _match;

    public MatchInfo MatchInfo => _match ?? throw new InvalidOperationException("Cannot access match info before first round");

    public bool IsInitialized => _match != null;

    public GameManager(int id, int acidRounds)
    {
        Id = id;
        AcidRounds = acidRounds;
    }

    public GameSnapshot? Read()
    {
        try
        {
            return GameSnapshot.Load(File.ReadAllLines($"./game/s{Id}_{Round}.txt"), Round).Also(state =>
            {
                _match ??= new MatchInfo
                {
                    BasePosition = state.Player.Position,
                    GridSize = state.Size
                };
            });
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task<GameSnapshot?> ReadAsync(CancellationToken token = default)
    {
        try
        {
            return GameSnapshot.Load(await File.ReadAllLinesAsync($"./game/s{Id}_{Round}.txt", token), Round).Also(state =>
            {
                _match ??= new MatchInfo
                {
                    BasePosition = state.Player.Position,
                    GridSize = state.Size
                };
            });
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Polls the current state from the simulator and initializes <see cref="MatchInfo"/>, if on the first round.
    /// </summary>
    public GameSnapshot Poll()
    {
        while (true)
        {
            var state = Read();

            if (state != null)
            {
                return state;
            }

            Thread.Sleep(10);
        }
    }

    /// <summary>
    ///     Submits the command list and increments the round counter.
    /// </summary>
    public void Submit(CommandState cl)
    {
        var str = cl.Serialize();
        File.WriteAllText($"./game/c{Id}_{Round}.txt", str);
        Round++;
    }

    public async Task SubmitAsync(CommandState cl, CancellationToken token = default)
    {
        var str = cl.Serialize();
        await File.WriteAllTextAsync($"./game/c{Id}_{Round}.txt", str, token);
        Round++;
    }
}