using System.Collections.Concurrent;
using Common;

namespace MapGenerator;

public sealed class CodexAnswer
{
    public Grid<TileType> Grid { get; init; }
    public int Seed { get; init; }
}

public sealed class Codex
{
    private readonly Vector2ds _mapSize;
    private readonly bool _fewerResources;
    private readonly int _rewindSeconds;
    private readonly int _threads;
    private readonly HashSet<Vector2ds> _eliminated = new();

    private readonly BlockingCollection<(Vector2ds, TileType)[]> _markerQueue = new();

    public CodexAnswer? Answer { get; private set; }
    
    public bool Failed { get; private set; }

    public int Candidates { get; private set; }

    public double GenerateProgress { get; private set; }

    public Codex(Vector2ds mapSize, bool fewerResources, int rewindSeconds, int threads)
    {
        _mapSize = mapSize;
        _fewerResources = fewerResources;
        _rewindSeconds = rewindSeconds;
        _threads = threads;
        var seedBase = Simulacru.TimeSeed();
        var thread = new Thread(() => RunCodex(seedBase));
        thread.Start();
    }

    private void RunCodex(int seedBase)
    {
        var grids = new List<Grid<TileType>>();
        var seeds = new Dictionary<Grid<TileType>, int>();
        var sync = new object();

        Parallel.For(0, _rewindSeconds, new ParallelOptions { MaxDegreeOfParallelism = _threads }, i =>
        {
            var seed = seedBase - i;

            var grid = Simulacru.Generate(_mapSize, seed, _fewerResources);

            lock (sync)
            {
                grids.Add(grid);
                seeds.Add(grid, seed);
                Candidates++;
                GenerateProgress = grids.Count / (double)_rewindSeconds;
            }
        });

        foreach (var markers in _markerQueue.GetConsumingEnumerable())
        {
            grids.RemoveAll(grid =>
            {
                for (var i = 0; i < markers.Length; i++)
                {
                    var (tile, type) = markers[i];

                    if (grid[tile] != type)
                    {
                        return true;
                    }
                }

                return false;
            });

            Candidates = grids.Count;

            if (grids.Count == 0)
            {
                Failed = true;
                return;
            }

            if (grids.Count == 1)
            {
                Answer = new CodexAnswer
                {
                    Grid = grids.First(),
                    Seed = seeds[grids.First()]
                };

                return;
            }
        }
    }


    public void EnqueueEliminate(IEnumerable<(Vector2ds, TileType)> markers)
    {
        if (Answer != null)
        {
            return;
        }

        var results = new List<(Vector2ds, TileType)>();

        foreach (var (tile, type) in markers)
        {
            if (_eliminated.Add(tile))
            {
                results.Add((tile, type));
            }
        }

        if (results.Count > 0)
        {
            _markerQueue.Add(results.ToArray());
        }
    }
}