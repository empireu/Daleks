using System.Diagnostics;
using Common;

namespace Daleks;

public sealed class BotConfig
{
    public float IronMultiplier = 1;
    public float OsmiumMultiplier = 5;
}

internal class Controller2
{
    public BotConfig Config { get; }
    public MatchInfo Match { get; }
    public int AcidRounds { get; }
    
    public TileWorld TileWorld { get; }

    // Tiles that were discovered at some point
    public readonly HashSet<Vector2di> DiscoveredTiles = new();

    // Ores that were viewed at some point. They are removed once mined or if their existence conflicts with an up-to-date observation
    // (this happens if they were mined by some other player or were destroyed by acid)
    public readonly Dictionary<Vector2di, TileType> PendingOres = new();

    public Controller2(MatchInfo match, int acidRounds, BotConfig config)
    {
        Config = config;
        Match = match;
        AcidRounds = acidRounds;
        TileWorld = new TileWorld(match.GridSize);
    }
}

internal class Controller
{
    private const int GridSearchGranularity = 7;
    private const int RoundSafety = 15;

    private static readonly List<(TileType, int)> MiningTargets = new()
    {
       ( TileType.Osmium, 0),
       ( TileType.Iron, 1)
    };

    private readonly Vector2di _gridSize;
    private readonly Grid<bool> _nonOreTiles;
    private readonly Vector2di _basePos;
    private readonly int _acidRounds;

    private readonly HashSet<Vector2di> _gridSearchPoints = new();
    private readonly int _initialGridPoints;

    private readonly List<(TileType, Vector2di)> _knownOres = new();

    private Vector2di MapCenter => _gridSize / 2;

    public Controller(Vector2di gridSize, Vector2di basePos, int acidRounds)
    {
        _gridSize = gridSize;
        _nonOreTiles = new Grid<bool>(gridSize);
        _basePos = basePos;
        _acidRounds = acidRounds;

        for (var y = 0; y < gridSize.Y; y+=GridSearchGranularity)
        {
            for (var x = 0; x < gridSize.X; x += GridSearchGranularity)
            {
                _gridSearchPoints.Add(new Vector2di(x, y));
            }
        }

        _initialGridPoints = _gridSearchPoints.Count;
    }

    private void Validate(CommandState cl)
    {
        if (cl.Tail.GridSize != _gridSize)
        {
            throw new Exception("Received invalid grid size");
        }
    }

    private void UpdateSearchData(CommandState cl, HashSet<Vector2di> view)
    {
        var state = cl.Tail;

        _gridSearchPoints.Remove(state.Player.Position);
        
        foreach (var viewPos in view)
        {
            var tile = state[viewPos];

            Debug.Assert(tile != TileType.Unknown);

            if (tile != TileType.Osmium && tile != TileType.Iron)
            {
                _nonOreTiles[viewPos] = true;
                _knownOres.RemoveAll(x => x.Item2 == viewPos);
            }
            else
            {
                if (_knownOres.All(x => x.Item2 != viewPos))
                {
                    _knownOres.Add((tile, viewPos));
                }
            }

            if (tile is TileType.Bedrock or TileType.Acid)
            {
                _gridSearchPoints.Remove(viewPos);
            }
        }
    }

    private static HashSet<Vector2di> GetTileView(CommandState cl, out HashMultiMap<TileType, Vector2di> mapping)
    {
        var results = new HashSet<Vector2di>();
        var state = cl.Tail;
        var queue = new Queue<Vector2di>();
        queue.Enqueue(state.Player.Position);

        var directions = Enum.GetValues<Direction>();

        mapping = new HashMultiMap<TileType, Vector2di>();

        while (queue.Count > 0)
        {
            var front = queue.Dequeue();

            if (!results.Add(front))
            {
                continue;
            }

            mapping.Add(state[front], front);

            foreach (var direction in directions)
            {
                var targetPos = front + direction;

                if (state.IsWithinBounds(targetPos) && state[targetPos] != TileType.Unknown)
                {
                    queue.Enqueue(targetPos);
                }
            }
        }

        return results;
    }

    private (Vector2di, List<Vector2di>)? _lastPath;

    private static List<Vector2di>? FindPath(Vector2di start, Vector2di end, GameState state)
    {
        var queue = new PriorityQueue<List<Vector2di>, int>();

        void Enqueue(List<Vector2di> path, Vector2di target) => queue.Enqueue(path, Vector2di.DistanceSqr(target, end));

        Enqueue(new List<Vector2di> { start }, start);

        var visited = new HashSet<Vector2di>();
        var directions = Enum.GetValues<Direction>();

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();

            var back = path[^1];

            if (back == end)
            {
                if (path.Count > 0)
                {
                    path.RemoveAt(0);
                }

                return path;
            }

            foreach (var direction in directions)
            {
                var neighbor = back + direction;

                if (!state.IsWithinBounds(neighbor))
                {
                    continue;
                }

                var tile = state[neighbor];

                if (tile is TileType.Bedrock or TileType.Acid or TileType.Robot)
                {
                    continue;
                }

                if (!visited.Add(neighbor))
                {
                    continue;
                }

                var list = new List<Vector2di>(path.Count + 1);
                list.AddRange(path);
                list.Add(neighbor);
                Enqueue(list, neighbor);
            }
        }

        return null;
    }

    private enum StepStatus
    {
        Failure,
        Running,
        Success
    }

    private StepStatus StepTowards(CommandState cl, Vector2di target, bool useDigging = true)
    {
        var playerPos = cl.Tail.Player.Position;

        if (playerPos == target)
        {
            _lastPath = null;
            return StepStatus.Success;
        }

        List<Vector2di>? path;

        if (_lastPath != null)
        {
            var (end, p) = _lastPath.Value;
            path = end == target ? p : FindPath(playerPos, target, cl.Tail);
            _lastPath = null;
        }
        else
        {
            path = FindPath(playerPos, target, cl.Tail);
        }

        if (path == null || path.Count == 0)
        {
            Console.WriteLine("Failed to pathfind");
            return StepStatus.Failure;
        }

        _lastPath = (target, path);

        var dir = playerPos.DirectionTo(path[0]);
        path.RemoveAt(0);

        if (cl.Tail.Neighbor(dir) is TileType.Bedrock && _lastPath != null && _lastPath.Value.Item1 == target && _lastPath.Value.Item2 == path)
        {
            _lastPath = null;
            return StepTowards(cl, target, useDigging);
        }

        cl.Move(dir);

        if (useDigging)
        {
            cl.Mine(dir);
        }

        Console.WriteLine($"STEPPING TOWARDS {cl.Tail.Neighbor(dir)}");

        return StepStatus.Running;
    }

    private bool MineView(CommandState cl, HashMultiMap<TileType, Vector2di> mapping)
    {
        var state = cl.Tail;

        var candidatesInView = new List<(TileType type, Vector2di pos)>();

        candidatesInView.AddRange(_knownOres);

        Console.WriteLine($"Known: {_knownOres.Count}");

        foreach (var (targetType, _) in MiningTargets)
        {
            if (mapping.Contains(targetType))
            {
                candidatesInView.Add((
                    targetType, 
                    mapping[targetType].MinBy(p => Vector2di.DistanceSqr(p, state.Player.Position))
                ));
            }
        }

        if (candidatesInView.Count == 0)
        {
            // No ore is in view
            Console.WriteLine("NO ORE IN VIEW");
            return false;
        }

        var minedTiles = new HashSet<Direction>();

        bool HasMineTile() => minedTiles.Count < cl.Head.Player.Dig;

        foreach (var (neighborDir, neighborPos) in Enum.GetValues<Direction>().Select(d => (d, state.Player.Position + d)))
        {
            if (!HasMineTile())
            {
                break;
            }

            if (candidatesInView.Any(c => c.pos == neighborPos))
            {
                minedTiles.Add(neighborDir);
            }
        }

        if (minedTiles.Count > 0)
        {
            foreach (var direction in minedTiles)
            {
                cl.Mine(direction);
            }

            Console.WriteLine("MINING...");
            
            return true;
        }        

        var target = candidatesInView.MinBy(c => Vector2di.DistanceSqr(c.pos, state.Player.Position));

        StepTowards(cl, target.pos, !Attack(cl));

        Console.WriteLine("STEPPING...");
        
        return true;
    }

    private bool Attack(CommandState cl)
    {
        var state = cl.Tail;
        foreach (var direction in Enum.GetValues<Direction>())
        {
            for (var dist = 1; dist <= state.Player.Attack; dist++)
            {
                var p = state.Player.Position + direction.Step() * dist;

                if (!state.IsWithinBounds(p))
                {
                    continue;
                }

                var tile = state[p];

                if (tile == TileType.Robot)
                {
                    cl.Attack(direction);
                    Console.WriteLine($"Attacking {direction}");
                    return true;
                }
            }
        }

        return false;
    }

    public void Update(CommandState cl)
    {
        Console.WriteLine($"Position: {cl.Tail.Player.Position}");

        Validate(cl);

        var viewedTiles = GetTileView(cl, out var viewMultimap);
        var state = cl.Tail;

        UpdateSearchData(cl, viewedTiles);

        Console.WriteLine($"Search progress: {((_initialGridPoints - _gridSearchPoints.Count) / (float)_initialGridPoints * 100f):F}%");
        Console.WriteLine("\n\n\n");

        Console.WriteLine("Performing upgrades:");
        
        if (state.Player.HasBattery)
        {
            if (cl.Heal())
            {
                Console.WriteLine("  +Healing");
            }

            if (cl.BuySight())
            {
                Console.WriteLine("  +Sight");
            }

            if (cl.BuyAttack())
            {
                Console.WriteLine("  +Attack");
            }
        }

        if (cl.Tail.Round < (_acidRounds - RoundSafety) && _gridSearchPoints.Count > 0)
        {
            Console.WriteLine("FARMING...\n");

            if (!state.Player.HasBattery && cl.CouldBuyBattery)
            {
                Console.WriteLine("Buying battery...");

                if (StepTowards(cl, _basePos) == StepStatus.Success)
                {
                    cl.BuyBattery();
                }

                return;
            }

            if (!MineView(cl, viewMultimap))
            {
                var targetLookpoint = _gridSearchPoints.MinBy(p => Vector2di.DistanceSqr(p, state.Player.Position));

                if (StepTowards(cl, targetLookpoint, !Attack(cl)) == StepStatus.Failure)
                {
                    _gridSearchPoints.Remove(targetLookpoint);
                }

                Console.WriteLine($"Next anchor: {targetLookpoint}");
            }
        }
        else
        {
            Console.WriteLine("RETREATING...\n");

            var acid = Enum.GetValues<Direction>().Any(d => state.Neighbor(d) == TileType.Acid);

            var attacked = false;

            if (!acid)
            {
                attacked = Attack(cl);
            }

            if (StepTowards(cl, MapCenter, !attacked) == StepStatus.Failure)
            {
                var dir = state.Player.Position.DirectionTo(MapCenter);
                
                cl.Move(dir);

                if (!attacked)
                {
                    cl.Mine(dir);
                }
            }
        }
    }
}