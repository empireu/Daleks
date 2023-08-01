using System.Diagnostics;
using System.Runtime.CompilerServices;
using Common;

namespace Daleks;

public interface IBotConfig
{
    Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; }
    Dictionary<TileType, float> CostMap { get; }
    UpgradeType[] UpgradeList { get; }
    int ReserveOsmium { get; }
    int RoundsMargin { get; }
}

public sealed class BotConfig : IBotConfig
{
    public static readonly IBotConfig Default = new BotConfig();

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

public readonly struct AttackInfo
{
    public Player Player { get; }
    public int Round { get; }
    public Vector2di TargetPos { get; }

    public AttackInfo(Player player, int round, Vector2di targetPos)
    {
        Player = player;
        Round = round;
        TargetPos = targetPos;
    }
}

public sealed class Log
{
    public Log(int depth, string text)
    {
        Depth = depth;
        Text = text;
    }

    public int Depth { get; }
    public string Text { get; }
}

public sealed class Bot
{
    public IBotConfig Config { get; }
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
    private readonly List<Log> _logs = new();
    private int _logLevel = 0;

    public IReadOnlySet<Vector2di> DiscoveredTiles => _discoveredTiles;
    public IReadOnlySet<Vector2di> UndiscoveredMiningCandidates => _undiscoveredMiningCandidates;
    public IReadOnlyDictionary<Vector2di, TileType> PendingOres => _pendingOres;
    public IReadOnlyList<Vector2di>? Path => _path;
    public IReadOnlyCollection<UpgradeType> UpgradeQueue => _upgradeQueue;

    public IReadOnlyList<Log> Logs => _logs;

    public Vector2di? NextMiningTile { get; private set; }

    public ExploreMode ExplorationMode { get; private set; } = ExploreMode.ClosestBase;

    private readonly List<AttackInfo> _attacks = new();

    public IReadOnlyList<AttackInfo> Attacks => _attacks;

    public Bot(MatchInfo match, IBotConfig config, int gameRounds)
    {
        AcidRounds = gameRounds;

        if (config.UpgradeList.Count(x => x == UpgradeType.Antenna) > 1)
        {
            throw new Exception("Cannot use multiple antenna upgrades");
        }

        if (config.UpgradeList.Contains(UpgradeType.Battery))
        {
            throw new Exception("Bot prohibits explicit battery upgrade");
        }

        Config = config;
        Match = match;
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

    private void Log(string text)
    {
        _logs.Add(new Log(_logLevel, text));
    }

    private void Indent()
    {
        ++_logLevel;
    }

    private void Unindent()
    {
        --_logLevel;
    }

    private void Log(Action body)
    {
        Indent();
        body();
        Unindent();
    }

    private void ResetExploreTarget()
    {
        _previousExploreTarget = null;
    }

    private void UpdateTiles(CommandState cl)
    {
        var playerPos = cl.Head.Player.Position;

        Indent();

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
                if (_pendingOres.TryAdd(tilePos, type))
                {
                    Log($"Discovered {type}@{tilePos}");
                }
            }
            else
            {
                _pendingOres.Remove(tilePos);
            }
        }

        Unindent();

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

    private bool Attack(CommandState cl, bool simulate = false)
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
                        _attacks.Add(new AttackInfo(cl.Tail.Player, cl.Tail.Round, p));
                    }
                  
                    return true;
                }

                if (tile is not TileType.Dirt)
                {
                    break;
                }
            }
        }

        return false;
    }

    private bool UpdateMining(CommandState cl)
    {
        var playerPos = cl.Head.Player.Position;

        NextMiningTile = PendingOres.Count > 0
            ? PendingOres.Keys.MinBy(x => Vector2di.DistanceSqr(x, playerPos))
            : GetNextExploreTarget(playerPos);

        if (!NextMiningTile.HasValue)
        {
            Log("Exhausted mining targets");
            return false;
        }

        Log($"Discovered {DiscoveredTiles.Count}/{UndiscoveredMiningCandidates.Count} candidates");

        var canAttack = Attack(cl, simulate: true);

        if (canAttack)
        {
            Log("Can attack whilst mining!");
        }

        var success = TryStepTowards(NextMiningTile.Value, cl, out var reached, useObstacleMining: true, useMiningFast: !canAttack);

        Log($"Step: {success}");

        if (!cl.HasAction && canAttack)
        {
            Log("Attacking!");
            Attack(cl);
            return true;
        }

        if (cl is { HasAction: false, Head.Player.HasAntenna: true } && NextMiningTile.Value != playerPos)
        {
            Log("Scanning");
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

        return true;
    }

    private bool UpdateBuyBattery(CommandState cl)
    {
        Log("Buying battery...");

        if (!TryStepTowards(Match.BasePosition, cl, out var reached))
        {
            Log("Failed to step!");
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
        // Center
        var retreatTarget = Match.GridSize / 2;

        // Detect box
        if (cl.Tail.DiscoveredTiles.Contains(retreatTarget))
        {
            if (TileMap.Tiles[retreatTarget] == TileType.Robot)
            {
                var box = true;

                foreach (var direction in Enum.GetValues<Direction>())
                {
                    if (TileMap.Tiles[retreatTarget].IsWalkable())
                    {
                        box = false;
                        break;
                    }
                }

                var player = cl.Tail.Player;

                if (box)
                {
                    Log("ENEMY BOX DETECTED!");

                    var candidates = new[]
                    {
                        retreatTarget + new Vector2di(-1, 1), // Top left
                        retreatTarget + new Vector2di(1, 1), // Top right
                        retreatTarget + new Vector2di(-1, -1), // Bottom left
                        retreatTarget + new Vector2di(1, -1) // Bottom right
                    }.OrderBy(c => Vector2di.DistanceSqr(c, player.Position));

                    var found = false;

                    foreach (var candidate in candidates)
                    {
                        if (!TileMap.Tiles[candidate].IsUnbreakable())
                        {
                            Log("Found candidate for anti-box retreat!");
                            retreatTarget = candidate;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Log("NO CANDIDATES FOR ESCAPE!");
                    }
                }
            }
        }

        if(!TryStepTowards(retreatTarget, cl, out var reached))
        {
            var attacked = Attack(cl);
            
            Log($"Failed to step to center! Attack: {attacked}");

            if (!attacked)
            {
                var dir = cl.Tail.Player.Position.DirectionTo(retreatTarget);
                
                cl.Move(dir);

                Log($"Fallback movement to {dir}");
                Indent();
                if (!cl.HasAction)
                {
                    Log("Mining...");
                    cl.Mine(dir);
                }
                Unindent();
            }
        }

        if (!reached)
        {
            return;
        }

        Log("Reached holdout");

        if (Attack(cl))
        {
            return;
        }

        // build box

        Log("Building box!");

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
        _logs.Clear();
        _logLevel = 0;

        UpdateTiles(cl);

        var player = cl.Head.Player;
        var retreatMode = AcidRounds - cl.Head.Round <= Config.RoundsMargin;
       
        if (player.HasBattery)
        {
            while (cl is { CanBuy: true, CouldHeal: true })
            {
                Log($"Heal@{cl.Tail.Player.Hp}");
                Debug.Assert(cl.Heal());
            }

            while (_upgradeQueue.Count > 0)
            {
                var upgrade = _upgradeQueue.Peek();

                bool CanSpendOsmium() => !retreatMode && cl.Tail.Player.OsmiumCount > Config.ReserveOsmium;

                if (upgrade is UpgradeType.Antenna)
                {
                    if (cl.Tail.Player.HasAntenna || retreatMode)
                    {
                        Debug.Assert(_upgradeQueue.Dequeue() == UpgradeType.Antenna);
                        continue;
                    }

                    if (cl.CouldBuyAntenna && CanSpendOsmium())
                    {
                        Debug.Assert(cl.BuyAntenna());
                        Debug.Assert(_upgradeQueue.Dequeue() == UpgradeType.Antenna);
                        Log("Bought antenna");
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
                        Log($"Upgraded {ability}");
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

        if (retreatMode)
        {
            Log("Retreating...");
            Indent();
            UpdateRetreat(cl);
            Unindent();
        }
        else
        {
            if (!player.HasBattery && cl.CouldBuyBattery)
            {
                if (UpdateBuyBattery(cl))
                {
                    if (cl.Tail.Player.HasBattery)
                    {
                        Log("Bought battery");
                        ExplorationMode = ExploreMode.Closest;
                    }

                    return;
                }
            }


            Log("Mining...");
            Indent();
            if (!UpdateMining(cl))
            {
                Log("Failed to mine!");
                Indent();
                UpdateRetreat(cl);
                Unindent();
            }
            Unindent();
        }
    }

    public enum ExploreMode
    {
        Closest,
        ClosestBase
    }
}