using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cheats;
using Common;
using MapGenerator;

namespace Daleks;

public interface IBotConfig
{
    Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; }
    float UtilityMultiplier { get; }
    Dictionary<TileType, float> CostMap { get; }
    float DiagonalPenalty { get; }
    UpgradeType[] UpgradeList { get; }
    int ReserveOsmium { get; }
    int RoundsMargin { get; }
    float PlayerOverrideCost { get; }
    public bool UseMemoryScanning { get; }
    public bool UseSeedSearch { get; }
    public int SeedSearchRewind { get; }
    public int SeedSearchThreads { get; }
}

public sealed class BotConfig : IBotConfig
{
    public static readonly IBotConfig Default = new BotConfig();

    public Dictionary<Bot.ExploreMode, (float Player, float Base)> ExploreCostMultipliers { get; set; } = new()
    {
        { Bot.ExploreMode.Closest, (1f, 0f) },
        { Bot.ExploreMode.ClosestBase, (1f, 1f) }
    };

    public float UtilityMultiplier { get; set; } = 0.5f;

    private const float BigCost = 10_000f;

    public Dictionary<TileType, float> CostMap { get; set; } = new()
    {
        { TileType.Unknown, -50f },
        { TileType.Dirt,    0f },
        { TileType.Stone,   5f },
        { TileType.Cobble,  5f },
        { TileType.Iron,    -1000f },
        { TileType.Osmium,  -10000f },
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
    public bool UseMemoryScanning { get; set; } = false;
    public bool UseSeedSearch { get; set; } = true;
    public int SeedSearchRewind { get; set; } = 1000;
    public int SeedSearchThreads { get; set; } = 6;
}

public readonly struct AttackInfo
{
    public Player Player { get; }
    public int Round { get; }
    public Vector2ds TargetPos { get; }

    public AttackInfo(Player player, int round, Vector2ds targetPos)
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
    public IBotConfig Config { get; }
    public MatchInfo Match { get; }
    public int AcidRounds { get; }

    public TileMap Map { get; }
    public ExplorationAnalyzer Exploration { get; }

    private readonly Hublou? _hublou;
    private readonly Codex? _codex;
    private bool _downloadedMap;

    // The 9 tiles at the center of the map:
    private readonly IReadOnlySet<Vector2ds> _centerTiles;

    // Tiles that were viewed at least once:
    private readonly HashSet<Vector2ds> _discoveredTiles = new();

    // Tiles that are undiscovered and could have ores (excludes bedrock edges)
    private readonly HashSet<Vector2ds> _undiscoveredMiningCandidates = new();

    // Ores that were seen:
    private readonly Dictionary<Vector2ds, TileType> _pendingOres = new();
    
    private readonly Queue<UpgradeType> _upgradeQueue = new();
    
    private readonly List<Log> _logsRound = new();
    
    private int _logLevel;

    public IReadOnlySet<Vector2ds> DiscoveredTiles => _discoveredTiles;
    public IReadOnlySet<Vector2ds> UndiscoveredMiningCandidates => _undiscoveredMiningCandidates;
    public IReadOnlyDictionary<Vector2ds, TileType> PendingOres => _pendingOres;

    /// <summary>
    ///     Gets the path currently being followed.
    /// </summary>
    public IReadOnlyList<Vector2ds>? Path { get; private set; }

    /// <summary>
    ///     Gets the list of upgrades in queue.
    /// </summary>
    public IReadOnlyCollection<UpgradeType> UpgradeQueue => _upgradeQueue;

    /// <summary>
    ///     Gets the logs that were generated last run.
    /// </summary>
    public IReadOnlyList<Log> LogsRound => _logsRound;

    /// <summary>
    ///     Gets the next tile to be mined, if one exists.
    /// </summary>
    public Vector2ds? NextMiningTile { get; private set; }

    /// <summary>
    ///     Gets the current exploration mode.
    /// </summary>
    public ExploreMode ExplorationMode { get; private set; } = ExploreMode.ClosestBase;

    // All attacks that were attempted:
    private readonly List<AttackInfo> _allAttacks = new();

    // Attacks attempted last round:
    private readonly List<AttackInfo> _attacksRound = new();

    /// <summary>
    ///     Gets all attacks that have been initiated so far.
    /// </summary>
    public IReadOnlyList<AttackInfo> AllAttacks => _allAttacks;

    /// <summary>
    ///     Gets the attacks that were attempted last round.
    /// </summary>
    public IReadOnlyList<AttackInfo> AttacksRound => _attacksRound;

    private readonly Dictionary<Vector2ds, SpottedPlayerInfo> _spottedPlayers = new();

    private int? _lastHp;

    // All damage taken:
    private readonly List<TakenDamageInfo> _allDamageTaken = new();
    
    // Damage taken last round:
    private readonly List<TakenDamageInfo> _damageTakenRound = new();

    /// <summary>
    ///     Gets all damage taken by the bot.
    /// </summary>
    public IReadOnlyList<TakenDamageInfo> AllDamageTaken => _allDamageTaken;

    /// <summary>
    ///     Gets damage taken by the bot last round.
    /// </summary>
    public IReadOnlyList<TakenDamageInfo> DamageTakenRound => _damageTakenRound;

    /// <summary>
    ///     Gets the players that were recently spotted.
    /// </summary>
    public IReadOnlyDictionary<Vector2ds, SpottedPlayerInfo> SpottedPlayers => _spottedPlayers;

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

        Map = new TileMap(match.GridSize, config.CostMap, config.DiagonalPenalty);
        Exploration = new ExplorationAnalyzer(Map);

        if (config.UseMemoryScanning)
        {
            _hublou = new Hublou(match.GridSize);
        }

        if (config.UseSeedSearch)
        {
            _codex = new Codex(
                match.GridSize, 
                match.GridSize.X == 89,
                config.SeedSearchRewind,
                config.SeedSearchThreads
            );
        }

        // Ignores bedrock edges:
        for (var i = 1; i < match.GridSize.X - 1; i++)
        {
            for (var j = 1; j < match.GridSize.Y - 1; j++)
            {
                _undiscoveredMiningCandidates.Add(new Vector2ds(i, j));
            }
        }

        foreach (var abilityType in config.UpgradeList)
        {
            _upgradeQueue.Enqueue(abilityType);
        }

        _centerTiles = new HashSet<Vector2ds>().Also(hs =>
        {
            var center = match.GridSize / 2;

            for (var i = -1; i <= 1; i++)
            {
                for (var j = -1; j <= 1; j++)
                {
                    hs.Add(center + new Vector2ds(i, j));
                }
            }
        });
    }

    private void Log(string text, LogType type = LogType.Info)
    {
        _logsRound.Add(new Log(_logLevel, text, type));
    }

    private void Indent()
    {
        ++_logLevel;
    }

    private void Unindent()
    {
        --_logLevel;
    }
    
    private void UpdateTiles(CommandState cl)
    {
        var playerPos = cl.Head.Player.Position;

        Indent();

        var codexMarkers = new List<(Vector2ds, TileType)>();

        foreach (var tilePos in cl.DiscoveredTiles)
        {
            if (tilePos == playerPos)
            {
                continue;
            }

            var type = cl.Tail[tilePos];

            _discoveredTiles.Add(tilePos);
            _undiscoveredMiningCandidates.Remove(tilePos);

            Map.Tiles[tilePos] = type;

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

            if (type is TileType.Stone or TileType.Iron or TileType.Osmium or TileType.Bedrock)
            {
                codexMarkers.Add((tilePos, type));
            }
        }

        _codex?.EnqueueEliminate(codexMarkers);

        Unindent();

        _pendingOres.Remove(playerPos);
        _undiscoveredMiningCandidates.Remove(playerPos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ExploreCost(Vector2ds player, Vector2ds target)
    {
        var (kp, kb) = Config.ExploreCostMultipliers[ExplorationMode];
     
        return kp * Vector2ds.DistanceF(player, target) + kb * Vector2ds.DistanceF(Match.BasePosition, target);
    }
    
    private Vector2ds? _exploreTarget;

    private bool Step(CommandState cl, IReadOnlyList<Vector2ds> path, bool useObstacleMining = true, bool useMiningFast = true)
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

        var pathQueue = new Queue<Vector2ds>(path.Take(cl.Head.Player.Movement + 1));

        pathQueue.Dequeue();

        while (pathQueue.Count > 0)
        {
            var nextPosition = pathQueue.Dequeue();
            var actualPosition = cl.Tail.Player.Position;
            var move = actualPosition.DirectionTo(nextPosition);

            if (!Map.Tiles[nextPosition].IsWalkable())
            {
                if (cl.Head.Player.Position == actualPosition)
                {
                    // First move
                    if (useObstacleMining)
                    {
                        cl.Mine(move);
                        return true;
                    }

                    // Unfortunate
                    return false;
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
    /// <returns>
    ///     True, if the step was performed successfully.
    ///     Otherwise, false. Failure happens if the target is invalid (blocked off or out of bounds) or <see cref="useObstacleMining"/> is false and the bot cannot step.
    /// </returns>
    /// <exception cref="Exception">Thrown if the player movement state was changed prior to calling this routine.</exception>
    private bool Step(Vector2ds target, CommandState cl, out bool reached, bool useObstacleMining = true, bool useMiningFast = true)
    {
        if (target == cl.Head.Player.Position)
        {
            reached = true;
            return true;
        }

        reached = false;

        var path = Map.FindPath(cl.Head.Player.Position, target);

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
        var explorationOptions = new ExplorationAnalyzer.Options
        {
            KCostPlayer = Config.ExploreCostMultipliers[ExplorationMode].Player,
            KCostBase = Config.ExploreCostMultipliers[ExplorationMode].Base,
            KUtility = Config.UtilityMultiplier,
            PlayerPosition = cl.Head.Player.Position,
            BasePosition = Match.BasePosition,
            VisionOffsets = Player.SightOffsets[cl.Head.Player.GetAbilityLevel(AbilityType.Sight)],
            MovementSpeed = cl.Head.Player.GetAbilityLevel(AbilityType.Movement)
        };

        Exploration.UpdateFrontiers(explorationOptions);

        var playerPos = cl.Head.Player.Position;

        NextMiningTile = PendingOres.Count > 0 
            ? PendingOres.Keys.MinBy(x => Vector2ds.DistanceSqr(x, playerPos)) 
            : Exploration.GetExplorationTarget(explorationOptions);

        if (!NextMiningTile.HasValue)
        {
            Log("Exhausted mining targets");
            return false;
        }

        Log($"Discovered {DiscoveredTiles.Count}, {UndiscoveredMiningCandidates.Count} remaining");

        var canAttack = Attack(cl, simulate: true);

        if (canAttack)
        {
            Log("Can attack whilst mining!", LogType.Warning);
        }

        var success = Step(
            NextMiningTile.Value, 
            cl, 
            out _, 
            useObstacleMining: true, 
            useMiningFast: !canAttack
        );

        Log($"Step: {success}");

        if (!cl.HasAction && canAttack)
        {
            Log("Attacking!"); // Though this probably won't work since we're probably moving and the enemy is also moving
            Attack(cl);
            return true;
        }

        if (cl is { HasAction: false, Head.Player.HasAntenna: true } && NextMiningTile.Value != playerPos)
        {
            Log("Scanning");

            cl.Scan(playerPos.DirectionTo(NextMiningTile.Value));
        }
        else if(cl.RemainingMines > 0)
        {
            // Mines extra blocks if possible (because it is free)
            // This is the only real use for the digging upgrade: collecting stone
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
            cl.BuyBattery();
        }

        return true;
    }

    private enum EnemyCenterStrategy
    {
        /// <summary>
        ///     Reported if no enemies were detected in the center region.
        ///     Enemies may still exist outside of the center region.
        /// </summary>
        None,

        /// <summary>
        ///     Reported if an enemy is camping in the center of the map and is actively placing blocks
        ///     to block other players.
        /// </summary>
        Boxing,

        /// <summary>
        ///     Reported if an enemy is in the center region of the map, but the strategy cannot be determined yet.
        /// </summary>
        Indeterminate
    }

    private Vector2ds AssessRetreatOptions(CommandState cl, out EnemyCenterStrategy enemyStrategy)
    {
        var target = Match.GridSize / 2;

        bool EnemiesAtCenter() => cl.DiscoveredTiles.Any(t => Map.Tiles[t].IsRobot() && _centerTiles.Contains(t));

        /*
         * Detects blocked-off center
         * This disables the boxing strategy implicitly.
         * - Indeterminate (if any players are at center)
         * - None
         */
        if (Map.Tiles[target].IsUnbreakable())
        {
            Log("Map center is unbreakable!", LogType.Warning);

            var queue = new PriorityQueue<Vector2ds, int>();
            queue.Enqueue(target, 0);
            var traversed = new HashSet<Vector2ds>();

            while (queue.Count > 0)
            {
                var front = queue.Dequeue();

                if (!traversed.Add(front))
                {
                    continue;
                }

                if (!Map.Tiles[front].IsUnbreakable())
                {
                    target = front;
                    Indent();
                    Log($"Falling back to {target}");
                    Unindent();
                    break;
                }

                for (var i = 0; i < 4; i++)
                {
                    var neighbor = front + (Direction)i;

                    if (Map.IsWithinBounds(neighbor))
                    {
                        var type = Map.Tiles[neighbor];

                        if (!type.IsUnbreakable())
                        {
                            queue.Enqueue(neighbor, type.IsWalkable() ? 0 : 1);
                        }
                    }
                }
            }

            // Boxing strategy is impossible now, so the only other options are None or Indeterminate

            // We'll choose Indeterminate if a player is in the center.

            // Consider tiles in view (so they are up-to-date)
            enemyStrategy =
                EnemiesAtCenter() 
                    ? EnemyCenterStrategy.Indeterminate 
                    : EnemyCenterStrategy.None;

            return target;
        }

        /*
         * Detects box
         * - Boxed (if player is surrounded by non-walkable tiles)
         * - Indeterminate (if a player is at the center center, but not boxed in)
         */
        if (cl.DiscoveredTiles.Contains(target) && Map.Tiles[target].IsRobot())
        {
            var isBoxed = Enum
                .GetValues<Direction>()
                .All(direction => !Map.Tiles[target + direction].IsWalkable());

            /*
             * A player is camping at the center of the map. If they are boxed in, it means they are employing a strategy to block players from
             * reaching the center. An example is mars_bot.
             * It is likely that tiles will be re-placed as soon as we break them. As such, we will redirect to a corner of the box.
             * The enemy will not be able to place there and we will be safe from acid.
             * This will result in a stalemate.
             */

            if (isBoxed)
            {
                var player = cl.Tail.Player;

                var candidates = new[]
                {
                        target + new Vector2ds(-1, 1),
                        target + new Vector2ds(1, 1),
                        target + new Vector2ds(-1, -1),
                        target + new Vector2ds(1, -1)
                }.Where(c => Map.CanAccess(player.Position, c))
                    .OrderBy(c => Vector2ds.DistanceSqr(c, player.Position));

                var cornerFound = false;

                foreach (var candidate in candidates)
                {
                    if (!Map.Tiles[candidate].IsUnbreakable()) // may be elided once we figure out the details of CanAccess
                    {
                        Log("Found candidate for anti-box retreat!", LogType.Warning);
                        target = candidate;
                        cornerFound = true;
                        break;
                    }
                }

                if (!cornerFound)
                {
                    Log("No anti-box candidates!", LogType.FTL);
                }

                enemyStrategy = EnemyCenterStrategy.Boxing;
            }
            else
            {
                // If center tile is robot, then enemies are at center implicitly.
                enemyStrategy = EnemyCenterStrategy.Indeterminate;
            }

            return target;
        }

        enemyStrategy = EnemiesAtCenter() ? EnemyCenterStrategy.Indeterminate : EnemyCenterStrategy.None;

        return target;
    }

    public void UpdateRetreat(CommandState cl)
    {
        var canAttack = Attack(cl, simulate: true);

        var isAcidCritical = Enum.GetValues<Direction>().Any(dir =>
        {
            var tile = cl.Head.Player.Position + dir;
            return Map.IsWithinBounds(tile) && Map.Tiles[tile] == TileType.Acid;
        });

        var retreatTarget = AssessRetreatOptions(cl, out var enemyStrategy);

        if (enemyStrategy != EnemyCenterStrategy.None)
        {
            Log($"Enemy strategy: {enemyStrategy}", LogType.Peril);
        }

        if (!_centerTiles.Contains(retreatTarget))
        {
            Log("Retreat target outside of safe zone!", LogType.FTL);
        }

        if (isAcidCritical)
        {
            Log("Acid is CRITICAL!", LogType.Peril);
        }

        /*
         * If can attack, and acid is not critical, spend our action by attacking.
         * Also try stepping towards the retreat target (will work if no blocks are in our way)
         */
        if (canAttack && !isAcidCritical)
        {
            // Attack when not in peril:
            Log("Attacking!", LogType.Warning);
            Attack(cl);
            Step(retreatTarget, cl, out _, false, false);
            return;
        }

        /*
         * Acid is critical, so we'll want to prioritize surviving instead of attacking. We'll spend our action going to the
         * retreat target.
         */
        if (canAttack && isAcidCritical)
        {
            Log("Prioritizing survival above attacking target!", LogType.Peril);
        }

        if (!Step(retreatTarget, cl, out var reached, useObstacleMining: true, useMiningFast: true))
        {
            // We're in hot water here, not sure how to handle that!
            // This should not happen.
            // Anyway, we'll attack so we don't just stand still.

            if (canAttack && !cl.HasAction)
            {
                Attack(cl);
            }

            Log($"Failed to step to center!", LogType.FTL);

            return;
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
            if ((Map.Tiles[playerPos + direction]).IsWalkable())
            {
                cl.Place(direction);

                return;
            }
        }

        Log($"Safe for ~{cl.Tail.Player.CobbleCount} rounds");
    }

    private void BeginLogFrame()
    {
        _logsRound.Clear();
        _logLevel = 0;
    }

    private void PrepareUpdate()
    {
        Path = null;
        Map.ClearCaches();
        _attacksRound.Clear();
    }

    private void UpdateCheats(CommandState cl)
    {
        if (_hublou == null)
        {
            return;
        }

        if (_hublou.Failed)
        {
            Log("Failed to breach dimensional barrier!", LogType.Peril);
            return;
        }

        if (_hublou.TryGetResult(out var download))
        {
            Log("Dimensional barrier has been breached!", LogType.Peril);
        }
        else
        {
            return;
        }

        if (_downloadedMap)
        {
            return;
        }

        _downloadedMap = true;

        // Loads the downloaded map, replacing previously seen tiles but not tiles in view.
        for (var y = 0; y < Map.Size.Y; y++)
        {
            for (var x = 0; x < Map.Size.X; x++)
            {
                var tile = new Vector2ds(x, y);

                if (tile == cl.Head.Player.Position)
                {
                    continue;
                }

                if (cl.DiscoveredTiles.Contains(tile))
                {
                    continue;
                }

                var absolute = download[tile];

                Map.Tiles[tile] = absolute;

                if (absolute is TileType.Iron or TileType.Osmium)
                {
                    _pendingOres.TryAdd(tile, absolute);
                }
            }
        }
    }

    private void UpdateCodex(CommandState cl)
    {
        if (_codex == null)
        {
            return;
        }

        if (_codex.Failed)
        {
            Log("Failed to find the answer to the ultimate question of life, the universe, and everything!", LogType.Peril);
            return;
        }

        if (_codex.Answer != null)
        {
            Log("Found the answer to the ultimate question of life, the universe, and everything!", LogType.Peril);
            Indent();
            Log($"It is not 42, it is {_codex.Answer.Seed}.", LogType.Peril);
            Unindent();
        }
        else
        {
            Log($"Secrets: {_codex.Candidates}", LogType.Warning);
            Indent();
            Log($"Deciphering... {(_codex.GenerateProgress * 100):F2}%", LogType.Warning);
            Unindent();
            return;
        }

        if (_downloadedMap)
        {
            return;
        }

        _downloadedMap = true;

        var grid = _codex.Answer.Grid;

        // Loads the generated map, but will not replace known tiles.
        for (var y = 0; y < Map.Size.Y; y++)
        {
            for (var x = 0; x < Map.Size.X; x++)
            {
                var tile = new Vector2ds(x, y);

                if (tile == cl.Head.Player.Position)
                {
                    continue;
                }

                if (Map.Tiles[tile] != TileType.Unknown)
                {
                    continue;
                }

                var candidate = grid[tile];

                Map.Tiles[tile] = candidate;

                if (candidate is TileType.Iron or TileType.Osmium)
                {
                    _pendingOres.TryAdd(tile, candidate);
                }
            }
        }
    }

    private void UpdateSpottedPlayers(CommandState cl)
    {
        foreach (var tile in cl.DiscoveredTiles)
        {
            _spottedPlayers.Remove(tile);
            var type = Map.Tiles[tile];

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

                if (Map.IsWithinBounds(pos))
                {
                    Map.CostOverride[pos] += Config.PlayerOverrideCost;
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
        BeginLogFrame();
        UpdateCheats(cl);
        PrepareUpdate();

        UpdateTiles(cl);

        UpdateCodex(cl);

        UpdateSpottedPlayers(cl);
        LoadEnemyCostOverrides();
        UpdateIncomingDamage(cl);
        RunDecisions(cl);
    }

    public enum ExploreMode
    {
        Closest,
        ClosestBase
    }
}