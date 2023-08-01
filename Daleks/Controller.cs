using System.Diagnostics;
using System.Runtime.CompilerServices;
using Common;

namespace Daleks;

public sealed class BotConfig
{
    public Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; set; } = new()
    {
        { Bot.ExploreMode.Closest, (1.0f, 0.0f) },
        { Bot.ExploreMode.ClosestBase, (1.0f, 2.5f) }
    };

    public Dictionary<TileType, float> CostMap { get; set; } = new()
    {
        { TileType.Acid, 10_000 },
        { TileType.Stone, 2.5f },
        { TileType.Cobblestone, 2.5f },
    };

    public UpgradeType[] UpgradeList { get; set; } = new[]
    {
        UpgradeType.Sight,
        UpgradeType.Movement,
        UpgradeType.Sight,
        UpgradeType.Movement,
        UpgradeType.Attack,
        UpgradeType.Attack
    };

    public int ReserveOsmium { get; set; } = 1;

    public int RoundsMargin { get; set; } = 15;
}

public sealed class Bot
{
    public BotConfig Config { get; }
    public MatchInfo Match { get; }
    public int AcidRounds { get; }
    public TileMap TileMap { get; }

    // Tiles that were discovered at some point
    private readonly HashSet<Vector2di> _discoveredTiles = new();
    private readonly HashSet<Vector2di> _undiscoveredMiningCandidates = new();

    // Ores that were viewed at some point. They are removed once mined or if their existence conflicts with an up-to-date observation
    // (this happens if they were mined by some other player or were destroyed by acid)
    private readonly Dictionary<Vector2di, TileType> _pendingOres = new();
    
    // Last used path (for display)
    private List<Vector2di>? _path;

    private readonly Queue<UpgradeType> _upgradeQueue = new();

    public IReadOnlySet<Vector2di> DiscoveredTiles => _discoveredTiles;
    public IReadOnlySet<Vector2di> UndiscoveredMiningCandidates => _undiscoveredMiningCandidates;
    public IReadOnlyDictionary<Vector2di, TileType> PendingOres => _pendingOres;
    public IReadOnlyList<Vector2di>? Path => _path;
    public IReadOnlyCollection<UpgradeType> UpgradeQueue => _upgradeQueue;

    public Vector2di? NextMiningTile { get; private set; }

    public ExploreMode ExplorationMode { get; private set; } = ExploreMode.ClosestBase;
    
    public Bot(MatchInfo match, int acidRounds, BotConfig config)
    {
        if (config.UpgradeList.Count(x => x == UpgradeType.Antenna) > 1)
        {
            throw new Exception("Cannot use multiple antenna upgrades");
        }

        if (config.UpgradeList.Contains(UpgradeType.Battery))
        {
            throw new Exception("Bot prohibits explicit battery upgrade");
        }

        acidRounds = 500;
        Config = config;
        Match = match;
        AcidRounds = acidRounds;
        TileMap = new TileMap(match.GridSize, config.CostMap);

        // Ignore bedrock edges
        for (var i = 1; i < match.GridSize.X - 1; i++)
        {
            for (var j = 1; j < match.GridSize.Y - 1; j++)
            {
                _undiscoveredMiningCandidates.Add(new Vector2di(i, j));
            }
        }

        foreach (var abilityType in config.UpgradeList)
        {
            _upgradeQueue.Enqueue(abilityType);
        }
    }

    private void ResetExploreTarget()
    {
        _previousExploreTarget = null;
    }

    private void UpdateTiles(CommandState cl)
    {
        var playerPos = cl.Head.Player.Position;

        foreach (var tilePos in cl.DiscoveredTiles)
        {
            if (tilePos == playerPos)
            {
                continue;
            }

            var type = cl.Tail[tilePos];

            _discoveredTiles.Add(tilePos);
            _undiscoveredMiningCandidates.Remove(tilePos);

            TileMap.Tiles[tilePos] = type;
            TileMap.SetExplored(tilePos);

            if (type is TileType.Iron or TileType.Osmium)
            {
                _pendingOres.TryAdd(tilePos, type);
            }
            else
            {
                _pendingOres.Remove(tilePos);
            }
        }

        _pendingOres.Remove(playerPos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float DistanceHeuristic(Vector2di player, Vector2di target)
    {
        var (kp, kb) = Config.ExploreCostMultipliers[ExplorationMode];
     
        return kp * Vector2di.Manhattan(player, target) + kb * Vector2di.Manhattan(Match.BasePosition, target);
    }

    // used in GetNextUnexploredTile
    private readonly PriorityQueue<Vector2di, float> _discoveryQueueCache = new();

    private Vector2di? GetNextUnexploredTile(Vector2di playerPos)
    {
        _discoveryQueueCache.Clear();

        foreach (var tile in _undiscoveredMiningCandidates)
        {
            if (tile == Match.BasePosition || tile == playerPos)
            {
                continue;
            }

            _discoveryQueueCache.Enqueue(tile, DistanceHeuristic(playerPos, tile));
        }

        while (_discoveryQueueCache.Count > 0)
        {
            var candidate = _discoveryQueueCache.Dequeue();

            if (TileMap.TryFindPath(playerPos, candidate, out _))
            {
                return candidate;
            }

            _undiscoveredMiningCandidates.Remove(candidate);
        }

        return null;
    }

    private Vector2di? _previousExploreTarget;

    private Vector2di? GetNextExploreTarget(Vector2di playerPos)
    {
        if (_previousExploreTarget.HasValue)
        {
            var t = _previousExploreTarget.Value;

            if (!TileMap.Tiles[t].IsUnbreakable() && _undiscoveredMiningCandidates.Contains(t))
            {
                return _previousExploreTarget;
            }
        }

        _previousExploreTarget = GetNextUnexploredTile(playerPos);
        
        return _previousExploreTarget;
    }

    private bool TryStepTowards(Vector2di target, CommandState cl, out bool reached, bool useObstacleMining = true, bool useMiningFast = true)
    {
        if (cl.Head.Player.Position != cl.Tail.Player.Position)
        {
            throw new Exception("Invalid player state for movement");
        }

        if (target == cl.Head.Player.Position)
        {
            reached = true;
            return true;
        }
        
        reached = false;

        if (!TileMap.TryFindPath(cl.Head.Player.Position, target, out var path))
        {
            Console.WriteLine("No path");
            return false;
        }

        _path = path;

        var pathQueue = new Queue<Vector2di>(path.Take(cl.Head.Player.Movement + 1));

        Debug.Assert(pathQueue.Dequeue() == cl.Head.Player.Position);

        while (pathQueue.Count > 0)
        {
            var nextPosition = pathQueue.Dequeue();
            var actualPosition = cl.Tail.Player.Position;
            var move = actualPosition.DirectionTo(nextPosition);

            if (!TileMap.Tiles[nextPosition].IsWalkable())
            {
                if (cl.Head.Player.Position == actualPosition)
                {
                    // First move
                    if (useObstacleMining)
                    {
                        cl.Mine(move);
                    }
                    return true;
                }

                // No more steps are possible
                return true;
            }

            cl.Move(move);

            if (!cl.Tail[cl.Tail.Player.Position + move].IsWalkable())
            {
                if (useMiningFast)
                {
                    cl.Mine(move);
                }
            }
        }

        return true;
    }

    private static bool Attack(CommandState cl, bool simulate = false)
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
                    if (!simulate)
                    {
                        cl.Attack(direction);
                    }
                  
                    return true;
                }
            }
        }

        return false;
    }

    private bool a;

    private void UpdateMining(CommandState cl)
    {
        var playerPos = cl.Head.Player.Position;

        NextMiningTile = PendingOres.Count > 0
            ? PendingOres.Keys.MinBy(x => Vector2di.DistanceSqr(x, playerPos))
            : GetNextExploreTarget(playerPos);

        if (!NextMiningTile.HasValue)
        {
            if (!a)
            {
                a = true;
                Console.WriteLine($"{cl.Head.Round} rnds");
            }
            return;
        }

        TryStepTowards(NextMiningTile.Value, cl, out var reached);

        if (cl is { HasAction: false, Head.Player.HasAntenna: true } && NextMiningTile.Value != playerPos)
        {
            cl.Scan(playerPos.DirectionTo(NextMiningTile.Value));
        }
        else
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                if (cl.IsMined(direction))
                {
                    continue;
                }

                if (cl.RemainingMines <= 0)
                {
                    break;
                }

                var type = cl.Tail[cl.Tail.Player.Position + direction];

                if (!type.IsWalkable() && !type.IsUnbreakable())
                {
                    cl.Mine(direction);
                }
            }
        }
    }

    private bool UpdateBuyBattery(CommandState cl)
    {
        if (!TryStepTowards(Match.BasePosition, cl, out var reached))
        {
            return false;
        }

        if (reached)
        {
            Debug.Assert(cl.BuyBattery());
            ResetExploreTarget();
        }

        return true;
    }

    public void UpdateRetreat(CommandState cl)
    {
        var center = Match.GridSize / 2;

        if(!TryStepTowards(center, cl, out var reached))
        {
            Console.WriteLine($"Failed to step to center!: {Attack(cl)}");
        }

        if (!reached)
        {
            Console.WriteLine("Not reached");
            return;
        }

        Console.WriteLine("Reached");

        if (Attack(cl))
        {
            Console.WriteLine("Attack");
            return;
        }

        // build box

        var playerPos = cl.Head.Player.Position;

        foreach (var direction in Enum.GetValues<Direction>())
        {
            if ((TileMap.Tiles[playerPos + direction]).IsWalkable())
            {
                cl.Place(direction);

                return;
            }
        }
    }

    public void Update(CommandState cl)
    {
        _path = null;

        UpdateTiles(cl);

        var player = cl.Head.Player;

        if (!player.HasBattery && cl.CouldBuyBattery)
        {
            if (UpdateBuyBattery(cl))
            {
                if (cl.Tail.Player.HasBattery)
                {
                    ExplorationMode = ExploreMode.Closest;
                }

                return;
            }
        }

        if (player.HasBattery)
        {
            while (cl is { CanBuy: true, CouldHeal: true })
            {
                Debug.Assert(cl.Heal());
            }

            while (_upgradeQueue.Count > 0)
            {
                var upgrade = _upgradeQueue.Peek();

                bool CanSpendOsmium() => cl.Tail.Player.OsmiumCount > Config.ReserveOsmium;

                if (upgrade is UpgradeType.Antenna)
                {
                    if (cl.Tail.Player.HasAntenna)
                    {
                        Debug.Assert(_upgradeQueue.Dequeue() == UpgradeType.Antenna);
                        continue;
                    }

                    if (cl.CouldBuyAntenna && CanSpendOsmium())
                    {
                        Debug.Assert(cl.BuyAntenna());
                        Debug.Assert(_upgradeQueue.Dequeue() == UpgradeType.Antenna);
                        continue;
                    }

                    break;
                }

                Debug.Assert(upgrade != UpgradeType.Battery);

                var actualLevel = cl.Tail.Player.GetUpgradeLevel(upgrade);

                Debug.Assert(actualLevel is >= 1 and <= 3);

                if (actualLevel == 3)
                {
                    Debug.Assert(_upgradeQueue.Dequeue() == upgrade);
                    continue;
                }

                var ability = upgrade switch
                {
                    UpgradeType.Sight => AbilityType.Sight,
                    UpgradeType.Attack => AbilityType.Attack,
                    UpgradeType.Drill => AbilityType.Drill,
                    UpgradeType.Movement => AbilityType.Movement,
                    _ => throw new ArgumentOutOfRangeException(null, $"Unexpected upgrade {upgrade}")
                };

                // Upgrade if osmium is not required or threshold is satisfied
                if (actualLevel == 1 || CanSpendOsmium()) 
                {
                    if (cl.UpgradeAbility(ability))
                    {
                        Debug.Assert(_upgradeQueue.Dequeue() == upgrade);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        if (AcidRounds - cl.Head.Round <= Config.RoundsMargin)
        {
            UpdateRetreat(cl);
        }
        else
        {
            UpdateMining(cl);
        }
    }

    public enum ExploreMode
    {
        Closest,
        ClosestBase
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

    private static List<Vector2di>? FindPath(Vector2di start, Vector2di end, GameSnapshot snapshot)
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

                if (!snapshot.IsWithinBounds(neighbor))
                {
                    continue;
                }

                var tile = snapshot[neighbor];

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
            if (mapping.ContainsKey(targetType))
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