using Common;

namespace Daleks;

public sealed class MatchInfo
{
    public Vector2di BasePosition { get; init; }
    public Vector2di GridSize { get; init; }
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

    /// <summary>
    ///     Polls the current state from the simulator and initializes <see cref="MatchInfo"/>, if on the first round.
    /// </summary>
    public GameState Read()
    {
        var path = $"./game/s{Id}_{Round}.txt";

        while (true)
        {
            try
            {
                return GameState.Load(File.ReadAllLines(path), Round).Also(state =>
                {
                    _match ??= new MatchInfo
                    {
                        BasePosition = state.Player.Position,
                        GridSize = state.GridSize
                    };
                });
            }
            catch (IOException)
            {
                Thread.Sleep(10);
            }
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
}