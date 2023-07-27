using System.Diagnostics;

namespace Daleks;

internal class Controller
{
    private const int GridSearchGranularity = 7;
    private const int RoundSafety = 10;

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

        _gridSearchPoints.Remove(state.Player.ActualPos);
        
        foreach (var viewPos in view)
        {
            var tile = state[viewPos];

            Debug.Assert(tile != TileType.Unknown);

            if (tile != TileType.Osmium && tile != TileType.Iron)
            {
                _nonOreTiles[viewPos] = true;
            }

            if (tile is TileType.Bedrock or TileType.Acid)
            {
                _gridSearchPoints.Remove(viewPos);
            }
        }
    }

    private static HashSet<Vector2di> GetTileView(CommandState cl, out MultiMap<TileType, Vector2di> mapping)
    {
        var results = new HashSet<Vector2di>();
        var state = cl.Tail;
        var queue = new Queue<Vector2di>();
        queue.Enqueue(state.Player.ActualPos);

        var directions = Enum.GetValues<Direction>();

        mapping = new MultiMap<TileType, Vector2di>();

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
                var targetPos = front + direction.Offset();

                if (state.IsWithinBounds(targetPos) && state[targetPos] != TileType.Unknown)
                {
                    queue.Enqueue(targetPos);
                }
            }
        }

        return results;
    }

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
        var playerPos = cl.Tail.Player.ActualPos;

        if (playerPos == target)
        {
            return StepStatus.Success;
        }

        var path = FindPath(playerPos, target, cl.Tail);

        if (path == null || path.Count < 2)
        {
            Console.WriteLine("Failed to pathfind");
            return StepStatus.Failure;
        }
        
        var dir = playerPos.DirectionTo(path[1]);

        cl.Move(dir);

        if (useDigging)
        {
            cl.Mine(dir);
        }

        Console.WriteLine($"STEPPING TOWARDS {cl.Tail.Neighbor(dir)}");

        return StepStatus.Running;
    }

    private bool MineView(CommandState cl, MultiMap<TileType, Vector2di> mapping)
    {
        var state = cl.Tail;

        var candidatesInView = new List<(TileType type, Vector2di pos, int priority)>();

        foreach (var (targetType, priority) in MiningTargets)
        {
            if (mapping.Contains(targetType))
            {
                candidatesInView.Add((
                    targetType, 
                    mapping[targetType].MinBy(p => Vector2di.DistanceSqr(p, state.Player.ActualPos)),
                    priority
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

        bool HasMineTile() => minedTiles.Count < cl.MineCount;

        foreach (var (neighborDir, neighborPos) in Enum.GetValues<Direction>().Select(d => (d, state.Player.ActualPos + d.Offset())))
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
            cl.Mine(minedTiles);
            Console.WriteLine("MINING...");
            return true;
        }        

        var target = candidatesInView.MinBy(c => Vector2di.DistanceSqr(c.pos, state.Player.ActualPos));

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
                var p = state.Player.ActualPos + direction.Offset() * dist;

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
        Console.WriteLine($"Position: {cl.Tail.Player.ActualPos}");

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

            if (!state.Player.HasBattery && cl.CanBuyBattery)
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
                var targetLookpoint = _gridSearchPoints.MinBy(p => Vector2di.DistanceSqr(p, state.Player.ActualPos));

                if (StepTowards(cl, targetLookpoint, !Attack(cl)) == StepStatus.Failure)
                {
                    _gridSearchPoints.Remove(targetLookpoint);
                }

                Console.WriteLine($"SEARCHING... {targetLookpoint}");
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
                var dir = state.Player.ActualPos.DirectionTo(MapCenter);
                
                cl.Move(dir);

                if (!attacked)
                {
                    cl.Mine(dir);
                }
            }
        }
    }
}