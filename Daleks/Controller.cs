using System.Diagnostics;
using System.Runtime.CompilerServices;
using Common;

namespace Daleks;

public interface IBotConfig
{
    Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; }
    Dictionary<TileType, float> CostMap { get; }
    float DiagonalPenalty { get; }
    UpgradeType[] UpgradeList { get; }
    int ReserveOsmium { get; }
    int RoundsMargin { get; }
    float PlayerOverrideCost { get; }
}

public sealed class BotConfig : IBotConfig
{
    public static readonly IBotConfig Default = new BotConfig();

    public Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; set; } = new()
    {
        { Bot.ExploreMode.Closest, (1f, 0f) },
        { Bot.ExploreMode.ClosestBase, (1f, 1f) }
    };

    private const float BigCost = 10_000f;

    public Dictionary<TileType, float> CostMap { get; set; } = new()
    {
        { TileType.Unknown, -50f },
        { TileType.Dirt,    0f },
        { TileType.Stone,   5f },
        { TileType.Cobble,  5f },
        { TileType.Iron,    -100f },
        { TileType.Osmium,  -1000f },
        { TileType.Base,    0f },
        { TileType.Acid,    BigCost },
        { TileType.Robot0,  BigCost },
        { TileType.Robot1,  BigCost },
        { TileType.Robot2,  BigCost },
        { TileType.Robot3,  BigCost },
        { TileType.Robot4,  BigCost }
    };

    public float DiagonalPenalty { get; set; } = 100f;

    public UpgradeType[] UpgradeList { get; set; } = 
    {
        UpgradeType.Sight,
        UpgradeType.Attack,
        UpgradeType.Movement,
        UpgradeType.Attack,
        UpgradeType.Sight,
        UpgradeType.Movement,
        UpgradeType.Antenna
    };

    public float PlayerOverrideCost { get; set; } = 1000;
    public int ReserveOsmium { get; set; } = 0;
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

public readonly struct SpottedPlayerInfo
{
    public int Round { get; }
    public TileType Id { get; }

    public SpottedPlayerInfo(int round, TileType id)
    {
        Round = round;
        Id = id;
    }
}

public readonly struct TakenDamageInfo
{
    public int Delta { get; }
    public int Round { get; }

    public TakenDamageInfo(int delta, int round)
    {
        Delta = delta;
        Round = round;
    }
}

public enum LogType
{
    Info,
    Warning,
    Peril,
    FTL
}

public sealed class Log
{
    public Log(int depth, string text, LogType type)
    {
        Depth = depth;
        Text = text;
        Type = type;
    }

    public int Depth { get; }
    public string Text { get; }
    public LogType Type { get; }
}

public sealed class Bot
{
    private const int SmallCluster = 50;

    public IBotConfig Config { get; }
    public MatchInfo Match { get; }
    public int AcidRounds { get; }

    private readonly TileMap _tileMap;

    public IReadOnlyGrid<TileType> Tiles => _tileMap;

    private readonly HashSet<Vector2di> _discoveredTiles = new();
    private readonly HashSet<Vector2di> _undiscoveredMiningCandidates = new();
    private readonly Dictionary<Vector2di, TileType> _pendingOres = new();
    
    private readonly Queue<UpgradeType> _upgradeQueue = new();
    private readonly List<Log> _logs = new();
    private int _logLevel = 0;

    /// <summary>
    ///     Gets the tile positions that were visited so far.
    /// </summary>
    public IReadOnlySet<Vector2di> DiscoveredTiles => _discoveredTiles;

    /// <summary>
    ///     Gets the tile positions that are yet to be visited.
    /// </summary>
    public IReadOnlySet<Vector2di> UndiscoveredMiningCandidates => _undiscoveredMiningCandidates;

    /// <summary>
    ///     Gets the tile positions of ores that were observed and have not been mined yet.
    /// </summary>
    public IReadOnlyDictionary<Vector2di, TileType> PendingOres => _pendingOres;

    /// <summary>
    ///     Gets the path currently being followed.
    /// </summary>
    public IReadOnlyList<Vector2di>? Path { get; private set; }

    /// <summary>
    ///     Gets the list of upgrades in queue.
    /// </summary>
    public IReadOnlyCollection<UpgradeType> UpgradeQueue => _upgradeQueue;

    /// <summary>
    ///     Gets the logs that were generated last run.
    /// </summary>
    public IReadOnlyList<Log> Logs => _logs;

    /// <summary>
    ///     Gets the next tile to be mined, if one exists.
    /// </summary>
    public Vector2di? NextMiningTile { get; private set; }

    /// <summary>
    ///     Gets the current exploration mode.
    /// </summary>
    public ExploreMode ExplorationMode { get; private set; } = ExploreMode.ClosestBase;

    private readonly List<AttackInfo> _allAttacks = new();

    /// <summary>
    ///     Gets all attacks that have been initiated so far.
    /// </summary>
    public IReadOnlyList<AttackInfo> AllAttacks => _allAttacks;

    private readonly List<AttackInfo> _attacksRound = new();

    public IReadOnlyList<AttackInfo> AttacksRound => _attacksRound;

    private readonly Dictionary<Vector2di, SpottedPlayerInfo> _spottedPlayers = new();

    private int? _lastHp;

    private readonly List<TakenDamageInfo> _allDamageTaken = new();

    public IReadOnlyList<TakenDamageInfo> AllDamageTaken => _allDamageTaken;

    private readonly List<TakenDamageInfo> _damageTakenRound = new();

    public IReadOnlyList<TakenDamageInfo> DamageTakenRound => _damageTakenRound;

    public IReadOnlyDictionary<Vector2di, SpottedPlayerInfo> SpottedPlayers => _spottedPlayers;

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
        _tileMap = new TileMap(match.GridSize, config.CostMap, config.DiagonalPenalty);

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

    private void Log(string text, LogType type = LogType.Info)
    {
        _logs.Add(new Log(_logLevel, text, type));
    }

    private void Indent()
    {
        ++_logLevel;
    }

    private void Unindent()
    {
        --_logLevel;
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

            _tileMap.Tiles[tilePos] = type;

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
        _undiscoveredMiningCandidates.Remove(playerPos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ExploreCost(Vector2di player, Vector2di target)
    {
        var (kp, kb) = Config.ExploreCostMultipliers[ExplorationMode];
     
        return kp * Vector2di.DistanceF(player, target) + kb * Vector2di.DistanceF(Match.BasePosition, target);
    }
    
    private Vector2di? GetUnexploredTile(Player player)
    {
        /*
         * Tiles are initially sorted using costs based on:
         *  - Distance to the player
         *      * Naturally the fastest tile to get to
         *  - Distance to the base
         *      * Keeps player closer to the base at the start of the round (so buying the battery is faster)
         *      * Makes the explored region evolve in a more cohesive fashion (instead of just going deep around the map)
         * P.S. They are sorted from worst to best
         *
         * A number of the best tiles are then evaluated using 2 criteria:
         *  - a. How many view tiles would be out of bounds if exploring the tile
         *      * Prevents crawling across the edge and wasting a lot of view tiles
         *  - b. How many view tiles would end up on already explored tiles
         *      * Is considered to be an efficiency metric
         *      * Encourages following the contour of the already explored region
         */

        var tiles = _undiscoveredMiningCandidates
            .OrderByDescending(x => ExploreCost(player.Position, x))
            .ToList();
        
        Vector2di? best = null;
        var bestCost = float.MaxValue;
        var offsets = Player.SightOffsets[player.Sight];
        
        var searched = 0;

        while (tiles.Count > 0 && searched++ < 32)
        {
            var candidate = tiles.Last(); // Worst to best
            tiles.RemoveAt(tiles.Count - 1);

            var cost = 0f;

            if (_tileMap.CanAccess(player.Position, candidate))
            {
                for (var index = 0; index < offsets.Length; index++)
                {
                    var target = candidate + offsets[index];

                    if (!_tileMap.IsWithinBounds(target))
                    {
                        // a.
                        cost += 0.25f;
                    }
                    else if (_tileMap.Tiles[target] != TileType.Unknown)
                    {
                        // b.
                        cost += 1f;
                    }
                }

                if (best == null || cost < bestCost)
                {
                    bestCost = cost;
                    best = candidate;
                }
            }
            else
            {
                _undiscoveredMiningCandidates.Remove(candidate);
            }
        }

        return best;
    }

    private Vector2di? _previousExploreTarget;

    private Vector2di? GetNextExploreTarget(Player p)
    {
        /*
        * Preventing gaps
        *  - Finding clusters of unexplored tiles with number of tiles below threshold
        *  - Prioritize exploring those
        */

        if (_tileMap.UnexploredClusters.Count > 0)
        {
            var smallest = _tileMap.UnexploredClusters.MinBy(x => x.Count)!;

            if (smallest.Count <= SmallCluster)
            {
                // Target barycenter
                Log($"Exploring cluster of {smallest.Count}", LogType.Warning);
                return Vector2di.BarycenterMany(smallest).Round();
            }
        }

        if (_previousExploreTarget.HasValue)
        {
            var t = _previousExploreTarget.Value;

            if (!_tileMap.Tiles[t].IsUnbreakable() && _undiscoveredMiningCandidates.Contains(t))
            {
                Log("Using cached target...");
                return _previousExploreTarget;
            }
        }

        Log("Getting new target!", LogType.Warning);

        _previousExploreTarget = GetUnexploredTile(p);
        
        return _previousExploreTarget;
    }

    private bool Step(
        CommandState cl, 
        IReadOnlyList<Vector2di> path, 
        bool useObstacleMining = true,
        bool useMiningFast = true)
    {
        if (cl.Head.Player.Position != cl.Tail.Player.Position)
        {
            throw new Exception("Invalid player state for movement");
        }

        if (path.First() != cl.Head.Player.Position)
        {
            throw new InvalidOperationException("Invalid path");
        }

        Path = path;

        Debug.Assert(path[0] == cl.Head.Player.Position);

        var pathQueue = new Queue<Vector2di>(path.Take(cl.Head.Player.Movement + 1));

        pathQueue.Dequeue();

        while (pathQueue.Count > 0)
        {
            var nextPosition = pathQueue.Dequeue();
            var actualPosition = cl.Tail.Player.Position;
            var move = actualPosition.DirectionTo(nextPosition);

            if (!_tileMap.Tiles[nextPosition].IsWalkable())
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

    /// <summary>
    ///     Tries to take a step towards <see cref="target"/>.
    /// </summary>
    /// <param name="target">A position to step towards. If an unbreakable tile is recorded at that position, this routine will fail.</param>
    /// <param name="cl"></param>
    /// <param name="reached">True, if the target position has been reached (the player is at the target location). Otherwise, false.</param>
    /// <param name="useObstacleMining">If true, mining will be used to clear tiles adjacent to the player. If false, the player will likely get stuck.</param>
    /// <param name="useMiningFast">If true, mining will be done in an ahead-of-time fashion.</param>
    /// <returns>True, if the step was performed successfully. Otherwise, false.</returns>
    /// <exception cref="Exception">Thrown if the player movement state was changed prior to calling this routine.</exception>
    private bool Step(Vector2di target, CommandState cl, out bool reached, bool useObstacleMining = true, bool useMiningFast = true)
    {
        if (target == cl.Head.Player.Position)
        {
            reached = true;
            return true;
        }

        reached = false;

        var path = _tileMap.FindPath(cl.Head.Player.Position, target);

        if (path == null)
        {
            Log($"No path was found to {target}", LogType.Warning);
            return false;
        }

        return Step(cl, path, useObstacleMining, useMiningFast);
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

                if (tile.IsRobot())
                {
                    if (!simulate)
                    {
                        cl.Attack(direction);
                        var info = new AttackInfo(cl.Tail.Player, cl.Tail.Round, p);
                        _allAttacks.Add(info);
                        _attacksRound.Add(info);
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
            : GetNextExploreTarget(cl.Head.Player);

        if (!NextMiningTile.HasValue)
        {
            Log("Exhausted mining targets");
            return false;
        }

        Log($"Discovered {DiscoveredTiles.Count}, {UndiscoveredMiningCandidates.Count} remaining");

        var canAttack = Attack(cl, simulate: true);

        if (canAttack)
        {
            Log("Can attack whilst mining!");
        }

        var success = Step(
            NextMiningTile.Value, 
            cl, 
            out var reached, 
            useObstacleMining: true, 
            useMiningFast: !canAttack
        );

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
                if (cl.IsMining(direction))
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

        if (!Step(Match.BasePosition, cl, out var reached))
        {
            Log("Failed to step!");
            return false;
        }

        if (reached)
        {
            var bought = cl.BuyBattery();
            Debug.Assert(bought);
            ResetExploreTarget();
        }

        return true;
    }

    public void UpdateRetreat(CommandState cl)
    {
        var retreatTarget = Match.GridSize / 2;
        var canAttack = Attack(cl, simulate: true);
        var isAcidDetected = cl.DiscoveredTilesMulti.ContainsKey(TileType.Acid);
        var isAcidCritical = Enum.GetValues<Direction>().Count(dir =>
        {
            var pos = cl.Head.Player.Position + dir;
            return Tiles.IsWithinBounds(pos) && Tiles[pos] == TileType.Acid;
        }) >= 2;

        // Detect box
        if (cl.Tail.DiscoveredTiles.Contains(retreatTarget))
        {
            if (_tileMap.Tiles[retreatTarget].IsRobot())
            {
                var box = true;

                /*
                 * A player is camping at the center of the map. If they are boxed in, it means they are employing a strategy to block players from
                 * reaching the center. An example is the strabun bot.
                 * It is likely that tiles will be re-placed as soon as we break them. As such, we will redirect to a corner of the box.
                 * The enemy will not be able to place there and we will be safe from acid.
                 * This will result in a stalemate.
                 */

                foreach (var direction in Enum.GetValues<Direction>())
                {
                    if (_tileMap.Tiles[retreatTarget + direction].IsWalkable())
                    {
                        box = false;
                        break;
                    }
                }

                if (box)
                {
                    Log("ENEMY BOX DETECTED!", LogType.Peril);

                    var player = cl.Tail.Player;

                    var candidates = new[]
                    {
                        retreatTarget + new Vector2di(-1, 1),
                        retreatTarget + new Vector2di(1, 1),
                        retreatTarget + new Vector2di(-1, -1),
                        retreatTarget + new Vector2di(1, -1)
                    }.Where(c => _tileMap.CanAccess(player.Position, c))
                        .OrderBy(c => Vector2di.DistanceSqr(c, player.Position));

                    var found = false;

                    foreach (var candidate in candidates)
                    {
                        if (!_tileMap.Tiles[candidate].IsUnbreakable()) // may be elided once we figure out the details of CanAccess
                        {
                            Log("Found candidate for anti-box retreat!", LogType.Warning);
                            retreatTarget = candidate;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Log("NO CANDIDATES FOR ESCAPE!", LogType.Peril);
                    }
                }
            }
        }

        // Detect blocked off center
        if (_tileMap.Tiles[retreatTarget].IsUnbreakable())
        {
            Log("Map center is unbreakable!", LogType.Warning);

            // Edge case that happens with a high probability on small maps.
            // We'll just search for the closest non-unbreakable tile.

            var queue = new Queue<Vector2di>();
            queue.Enqueue(retreatTarget);
            var traversed = new HashSet<Vector2di>();

            while (queue.Count > 0)
            {
                var front = queue.Dequeue();

                if (!traversed.Add(front))
                {
                    continue;
                }

                if (!_tileMap.Tiles[front].IsUnbreakable())
                {
                    retreatTarget = front;
                    Indent();
                    Log($"Falling back to {retreatTarget}");
                    Unindent();
                    break;
                }

                for (var i = 0; i < 4; i++)
                {
                    var neighbor = front + (Direction)i;

                    if (_tileMap.IsWithinBounds(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        if (isAcidCritical)
        {
            Log("Acid is CRITICAL!", LogType.Peril);
        }

        if (canAttack && !isAcidCritical)
        {
            // Attack when not in peril

            Log("Pre-emptively attacking!", LogType.Warning);
            Attack(cl);
            Step(retreatTarget, cl, out _, false, false);
            return;
        }

        if (canAttack && isAcidCritical)
        {
            Log("Prioritizing survival above attacking target", LogType.Warning);
        }

        if (!Step(retreatTarget, cl, out var reached, useObstacleMining: !canAttack || isAcidDetected, useMiningFast: !canAttack))
        {
            var attacked = Attack(cl);
            
            Log($"Failed to step to center! Attack: {attacked}", LogType.FTL);
        }

        if (!reached)
        {
            Log("Waiting to reach...");
            return;
        }

        Log("Reached holdout");

        if (Attack(cl))
        {
            Log("Successfully attacked in holdout!");
            return;
        }

        Log("Building box!", LogType.Warning);

        var playerPos = cl.Head.Player.Position;

        foreach (var direction in Enum.GetValues<Direction>())
        {
            if ((_tileMap.Tiles[playerPos + direction]).IsWalkable())
            {
                cl.Place(direction);

                return;
            }
        }

        Log($"Safe for ~{cl.Tail.Player.CobbleCount} rounds");
    }

    private void PrepareUpdate()
    {
        Path = null;
        _logs.Clear();
        _logLevel = 0;

        _tileMap.BeginFrame();
        _attacksRound.Clear();
    }

    private void UpdateSpottedPlayers(CommandState cl)
    {
        foreach (var tile in cl.DiscoveredTiles)
        {
            _spottedPlayers.Remove(tile);
            var type = Tiles[tile];

            if (type.IsRobot())
            {
                _spottedPlayers.Add(tile, new SpottedPlayerInfo(cl.Head.Round, type));
            }
        }

        var players = SpottedPlayers.Keys.ToArray();

        foreach (var tile in players)
        {
            if (cl.Head.Round - _spottedPlayers[tile].Round > 5)
            {
                _spottedPlayers.Remove(tile);
            }
        }
    }

    private void LoadEnemyCostOverrides()
    {
        if (_spottedPlayers.Count == 0)
        {
            return;
        }

        Log("Spotted players:");

        Indent();

        foreach (var tile in _spottedPlayers.Keys)
        {
            var round = _spottedPlayers[tile].Round;

            Log($"{tile} - round {round}");

            foreach (var offset in Player.SightOffsets[3])
            {
                var pos = tile + offset;

                if (_tileMap.IsWithinBounds(pos))
                {
                    _tileMap.CostOverride[pos] += Config.PlayerOverrideCost;
                }
            }
        }

        Unindent();
    }

    private void RunDecisions(CommandState cl)
    {
        var player = cl.Head.Player;
        var isRetreat = AcidRounds - cl.Head.Round <= Config.RoundsMargin;

        if (player.HasBattery)
        {
            while (cl is { CanBuy: true, CouldHeal: true } && player.Hp < 10)
            {
                var healed = cl.Heal();
                Log($"Healing @ {cl.Tail.Player.Hp} {healed}");
                Debug.Assert(healed);
            }

            while (_upgradeQueue.Count > 0)
            {
                var upgrade = _upgradeQueue.Peek();
                Debug.Assert(upgrade != UpgradeType.Battery);

                Log($"Next upgrade: {upgrade}");

                bool CanSpendOsmium() => !isRetreat && cl.Tail.Player.OsmiumCount > Config.ReserveOsmium;

                if (upgrade is UpgradeType.Antenna)
                {
                    if (cl.Tail.Player.HasAntenna || isRetreat)
                    {
                        _upgradeQueue.Dequeue();
                        continue;
                    }

                    if (cl.CouldBuyAntenna && CanSpendOsmium())
                    {
                        cl.BuyAntenna();
                        _upgradeQueue.Dequeue();
                        Log("Bought antenna");
                        continue;
                    }

                    break;
                }

                var actualLevel = cl.Tail.Player.GetUpgradeLevel(upgrade);

                Debug.Assert(actualLevel is >= 1 and <= 3);

                if (actualLevel == 3)
                {
                    _upgradeQueue.Dequeue();
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

                // Upgrade if osmium is not required (level 1 -> 2) or osmium threshold is satisfied
                // Also, spending osmium is not allowed when retreating (more valuable for healing)
                if (actualLevel == 1 || CanSpendOsmium())
                {
                    if (cl.UpgradeAbility(ability))
                    {
                        _upgradeQueue.Dequeue();

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

        if (isRetreat)
        {
            Log("Retreating...", LogType.Warning);
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
                Log("Failed to mine!", LogType.FTL);
                Indent();
                UpdateRetreat(cl);
                Unindent();
            }
            Unindent();
        }
    }

    private void UpdateIncomingDamage(CommandState cl)
    {
        var actualHp = cl.Head.Player.Hp;

        if (!_lastHp.HasValue)
        {
            _lastHp = actualHp;
            return;
        }

        _damageTakenRound.Clear();

        if (actualHp < _lastHp.Value)
        {
            var info = new TakenDamageInfo(_lastHp.Value - actualHp, cl.Head.Round);
            _allDamageTaken.Add(info);
            _damageTakenRound.Add(info);
        }

        _lastHp = actualHp;
    }

    public void Update(CommandState cl)
    {
        PrepareUpdate();
        UpdateTiles(cl);
        UpdateSpottedPlayers(cl);
        LoadEnemyCostOverrides();
        UpdateIncomingDamage(cl);
        RunDecisions(cl);
        EndUpdate();
    }

    private void EndUpdate()
    {
        _tileMap.EndFrame();
    }

    public enum ExploreMode
    {
        Closest,
        ClosestBase
    }
}