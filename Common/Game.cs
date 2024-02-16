using System.Text;

namespace Common;

public readonly struct Player
{
    public static readonly IReadOnlyDictionary<int, int> SightDiameters = new Dictionary<int, int>()
    {
        { 1, 5 },
        { 2, 7 },
        { 3, 9 }
    };

    public static readonly IReadOnlyDictionary<int, Vector2ds[]> SightOffsets = SightDiameters.ToDictionary(kvp => kvp.Key, kvp =>
    {
        var radius = SightDiameters[kvp.Key] / 2;
        var results = new List<Vector2ds>();

        var magic = kvp.Key switch
        {
            1 => Math.Sqrt(5.0) + 0.1,
            2 => Math.Sqrt(12.0) + 0.1,
            3 => Math.Sqrt(20.0) + 0.1,
            _ => throw new ArgumentOutOfRangeException()
        };

        for (var i = -radius; i <= radius; i++)
        {
            for (var j = -radius; j <= radius; j++)
            {
                if (i == 0 && j == 0)
                {
                    continue;
                }

                var v = new Vector2ds(i, j);

                if (v.Norm > magic)
                {
                    continue;
                }

                results.Add(v);
            }
        }

        return results.ToArray();
    });

    public static readonly IReadOnlyDictionary<int, Vector2ds[]> SightContours = SightOffsets.ToDictionary(kvp => kvp.Key, kvp =>
    {
        var disc = kvp.Value;
        var results = new List<Vector2ds>();

        foreach (var item in kvp.Value)
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var neighbor = item + direction;

                if (neighbor == Vector2ds.Zero)
                {
                    continue;
                }

                if (!disc.Contains(neighbor))
                {
                    results.Add(item);
                    break;
                }
            }
        }

        return results.ToArray();
    });

    public Vector2ds Position { get; init; }
    public int Hp { get; init; }
    public int Dig { get; init; }
    public int Attack { get; init; }
    public int Movement { get; init; }
    public int Sight { get; init; }
    public bool HasAntenna { get; init; }
    public bool HasBattery { get; init; }
    public int CobbleCount { get; init; }
    public int IronCount { get; init; }
    public int OsmiumCount { get; init; }

    public int GetAbilityLevel(AbilityType type) => type switch
    {
        AbilityType.Movement => Movement,
        AbilityType.Drill => Dig,
        AbilityType.Attack => Attack,
        AbilityType.Sight => Sight,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public int GetUpgradeLevel(UpgradeType type) => type switch
    {
        UpgradeType.Sight => Sight,
        UpgradeType.Attack => Attack,
        UpgradeType.Drill => Dig,
        UpgradeType.Movement => Movement,
        UpgradeType.Antenna => HasAntenna ? 1 : 0,
        UpgradeType.Battery => HasBattery ? 1 : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public Player WithAbilityLevel(AbilityType type, int level) => type switch
    {
        AbilityType.Movement => this with { Movement = level },
        AbilityType.Drill => this with { Dig = level },
        AbilityType.Attack => this with { Attack = level },
        AbilityType.Sight => this with { Sight = level },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}

public sealed class CommandState
{
    public Vector2ds BasePosition { get; }

    public CommandState(GameSnapshot headSnapshot, Vector2ds basePosition)
    {
        BasePosition = basePosition;
        _states = new List<GameSnapshot> { headSnapshot };
    }

    private readonly List<ActionCommand> _actions = new();
    private readonly List<Direction> _moves = new();
    private readonly List<BuyType> _upgrades = new();
    private readonly List<GameSnapshot> _states;

    public IReadOnlyList<ActionCommand> Actions => _actions;
    public IReadOnlyList<Direction> Moves => _moves;
    public IReadOnlyList<BuyType> Upgrades => _upgrades;
    public IReadOnlyList<GameSnapshot> States => _states;

    public GameSnapshot Head => _states.First();
    public GameSnapshot Tail => _states.Last();

    public IReadOnlySet<Vector2ds> DiscoveredTiles => Head.DiscoveredTiles;
    public IReadOnlyMultiMap<TileType, Vector2ds> DiscoveredTilesMulti => Head.DiscoveredTilesMulti;

    public bool HasAction => _actions.Any();

    private void ValidateExclusiveAction()
    {
        if (HasAction)
        {
            throw new Exception("Only one action is allowed per round");
        }
    }

    // Head because upgrades are applied next round
    public int RemainingMovements => Head.Player.Movement - _moves.Count;
    public int RemainingMines => Head.Player.Dig - (_actions.Count == 0 ? 0 : _actions.Sum(x => x.Type == ActionType.Mine ? 1 : 0));
    public bool CanMove => RemainingMovements > 0;

    // Head because upgrades and movements are applied next round
    /// <summary>
    ///     True if the player can buy upgrades (has battery or is at the base). Otherwise, false.
    /// </summary>
    public bool CanBuy => Head.Player.HasBattery || Head.Player.Position == BasePosition;

    /// <summary>
    ///     Adds a move towards the specified direction.
    /// </summary>
    /// <param name="dir"></param>
    /// <returns>True if the move can be applied next round. Otherwise, false. <b>This does not check for collisions with tiles.</b></returns>
    public bool Move(Direction dir)
    {
        if (!CanMove)
        {
            return false;
        }

        PushState(p => p with
        {
            Position = p.Position + dir
        });

        _moves.Add(dir);

        return true;
    }

    public bool IsMining(Direction direction) => _actions.Any(a => a.Type == ActionType.Mine && a.Dir == direction);

    public bool Mine(Direction direction)
    {
        if (_actions.Any(a => a.Type != ActionType.Mine))
        {
            throw new Exception($"Cannot use mine action over {string.Join(", ", Actions)}");
        }

        if (IsMining(direction))
        {
            return false;
        }

        if (RemainingMines < 0)
        {
            throw new InvalidOperationException("Invalid mine state");
        }

        if (RemainingMines == 0)
        {
            return false;
        }

        _actions.Add(new ActionCommand(ActionType.Mine, direction));

        return true;
    }

    public bool Place(Direction direction)
    {
        if (!CouldPlace)
        {
            return false;
        }

        ValidateExclusiveAction();

        PushState(p => p with
        {
            CobbleCount = p.CobbleCount - 1
        });

        _actions.Add(new ActionCommand(ActionType.Place, direction));

        return true;
    }

    public void Attack(Direction dir)
    {
        ValidateExclusiveAction();

        _actions.Add(new ActionCommand(ActionType.Attack, dir));
    }

    // Tail because the inventory may be modified by other things
    public bool CouldHeal => Tail.Player is { OsmiumCount: > 0, Hp: < 15 };
    public bool CouldBuyBattery => Tail.Player is { IronCount: >= 1, OsmiumCount: >= 1 };
    public bool CouldBuyAntenna => Tail.Player is { IronCount: >= 2, OsmiumCount: >= 1 };
    public bool CouldPlace => Tail.Player is { CobbleCount: >= 1 };

    public bool BuyBattery()
    {
        if (!CanBuy || Tail.Player.HasBattery || !CouldBuyBattery)
        {
            return false;
        }

        PushState(p => p with
        {
            HasBattery = true,
            OsmiumCount = Tail.Player.OsmiumCount - 1,
            IronCount = Tail.Player.IronCount - 1
        });

        _upgrades.Add(BuyType.Battery);

        return true;
    }

    public bool BuyAntenna()
    {
        if (!CanBuy || Tail.Player.HasAntenna || !CouldBuyAntenna)
        {
            return false;
        }

        PushState(p => p with
        {
            HasAntenna = true,
            OsmiumCount = Tail.Player.OsmiumCount - 1,
            IronCount = Tail.Player.IronCount - 2
        });

        _upgrades.Add(BuyType.Antenna);

        return true;
    }

    public bool UpgradeAbility(AbilityType type)
    {
        if (!CanBuy)
        {
            return false;
        }

        var level = Tail.Player.GetAbilityLevel(type);

        if (level == 3)
        {
            return false;
        }

        var upgrade = type switch 
        {
            AbilityType.Movement => BuyType.Movement,
            AbilityType.Drill => BuyType.Drill,
            AbilityType.Attack => BuyType.Attack,
            AbilityType.Sight => BuyType.Sight,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unexpected ability {type}")
        };


        if (level == 1 && Tail.Player is { IronCount: >= 3 })
        {
            PushState(p => p.WithAbilityLevel(type, 2) with
            {
                IronCount = p.IronCount - 3
            });

            _upgrades.Add(upgrade);

            return true;
        }

        if (level == 2 && Tail.Player is { IronCount: >= 6, OsmiumCount: >= 1 })
        {
            PushState(p => p.WithAbilityLevel(type, 3) with
            {
                IronCount = p.IronCount - 6,
                OsmiumCount = p.OsmiumCount - 1
            });

            _upgrades.Add(upgrade);

            return true;
        }

        return false;
    }

    private void PushState(GameSnapshot snapshot) => _states.Add(snapshot);

    private void PushState(Func<Player, Player> transform) => PushState(Tail.Bind(transform(Tail.Player)));

    public bool Heal()
    {
        if (!CanBuy || !CouldHeal)
        {
            return false;
        }

        PushState(p => p with
        {
            Hp = Math.Min(Tail.Player.Hp + 5, 15),
            OsmiumCount = Tail.Player.OsmiumCount - 1
        });

        _upgrades.Add(BuyType.Heal);

        return true;
    }

    public void Scan(Direction dir)
    {
        ValidateExclusiveAction();

        if (!Head.Player.HasAntenna)
        {
            throw new Exception("Cannot activate antenna");
        }

        _actions.Add(new ActionCommand(ActionType.Scan, dir));
    }

    public string Serialize()
    {
        var sb = new StringBuilder();

        void Append(string s)
        {
            sb.Append(s);
            sb.Append(' ');
        }

        foreach (var moveDir in _moves)
        {
            Append(moveDir.ToString());
        }

        foreach (var action in _actions)
        {
            Append(action.Type switch
            {
                ActionType.Attack => "A",
                ActionType.Mine => "M",
                ActionType.Scan => "S",
                ActionType.Place => "P",
                _ => throw new ArgumentOutOfRangeException()
            });

            Append(action.Dir.ToString());
        }

        foreach (var upgradeType in _upgrades)
        {
            Append("B");

            Append(upgradeType switch
            {
                BuyType.Sight => "S",
                BuyType.Attack => "A",
                BuyType.Drill => "D",
                BuyType.Movement => "M",
                BuyType.Antenna => "R",
                BuyType.Battery => "B",
                BuyType.Heal => "H",
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        return sb.ToString();
    }
}

public sealed class ActionCommand
{
    public ActionType Type { get; }
    public Direction Dir { get; }

    public ActionCommand(ActionType type, Direction dir)
    {
        Type = type;
        Dir = dir;
    }
}

public enum AbilityType : byte
{
    Movement,
    Drill,
    Attack,
    Sight,
}

public enum BuyType : byte
{
    Sight,
    Attack,
    Drill,
    Movement,
    Antenna,
    Battery,
    Heal
}

public enum UpgradeType : byte
{
    Sight,
    Attack,
    Drill,
    Movement,
    Antenna,
    Battery,
}

public enum ActionType : byte
{
    Attack,
    Mine,
    Scan,
    Place
}

public enum TileType : byte
{
    Unknown,
    Dirt,
    Stone,
    Cobble,
    Bedrock,
    Iron,
    Osmium,
    Base,
    Acid,
    Robot0,
    Robot1,
    Robot2,
    Robot3,
    Robot4,
}

public sealed class GameSnapshot : IReadOnlyGrid<TileType>
{
    private readonly Grid<TileType> _grid;
    private readonly HashSet<Vector2ds> _discoveredTiles = new();
    private readonly HashMultiMap<TileType, Vector2ds> _discoveredTilesMulti = new();

    public IReadOnlyGrid<TileType> Grid => _grid;
    public IReadOnlyList<TileType> Cells => _grid.Cells;
    public Vector2ds Size => _grid.Size;
    public IReadOnlySet<Vector2ds> DiscoveredTiles => _discoveredTiles;
    public IReadOnlyMultiMap<TileType, Vector2ds> DiscoveredTilesMulti => _discoveredTilesMulti;

    public int Round { get; }

    private GameSnapshot(Grid<TileType> grid, int round, HashSet<Vector2ds> discoveredTiles, HashMultiMap<TileType, Vector2ds> discoveredTilesMulti)
    {
        _grid = grid;
        _discoveredTiles = discoveredTiles;
        _discoveredTilesMulti = discoveredTilesMulti;
        Round = round;
    }

    private GameSnapshot(Vector2ds gridSize, int round)
    {
        _grid = new Grid<TileType>(gridSize);
        Round = round;
    }

    public Player Player { get; private set; }

    public TileType this[int x, int y] => _grid[x, y];
    public TileType this[Vector2ds v] => _grid[v.X, v.Y];
    public bool IsWithinBounds(int x, int y) => _grid.IsWithinBounds(x, y);
    public bool IsWithinBounds(Vector2ds v) => _grid.IsWithinBounds(v);
    
    public GameSnapshot Bind(Player player)
    {
        return new GameSnapshot(_grid, Round, _discoveredTiles, _discoveredTilesMulti)
        {
            Player = player
        };
    }

    private void ScanView()
    {
        var queue = new Queue<Vector2ds>();

        queue.Enqueue(Player.Position);

        while (queue.Count > 0)
        {
            var front = queue.Dequeue();

            if (!_discoveredTiles.Add(front))
            {
                continue;
            }

            _discoveredTilesMulti.Add(this[front], front);

            for (byte i = 0; i < 4; i++)
            {
                var direction = (Direction)i;

                var targetPos = front + direction;

                if (IsWithinBounds(targetPos) && this[targetPos] != TileType.Unknown)
                {
                    queue.Enqueue(targetPos);
                }
            }
        }
    }

    #region Parser

    private static Vector2ds PopTuple(ref Span<string> lines)
    {
        var result = lines[0].Map(str =>
        {
            var tokens = str.Split(' ');

            return new Vector2ds(int.Parse(tokens[0]), int.Parse(tokens[1]));
        });

        lines = lines[1..];

        return result;
    }

    public static GameSnapshot Load(Span<string> lines, int round)
    {
        var size = PopTuple(ref lines);
        var state = new GameSnapshot(size, round);

        for (var y = 0; y < size.Y; y++)
        {
            var line = lines[y].Replace(" ", "");

            for (var x = 0; x < size.X; x++)
            {
                var c = line[x];

                state._grid[x, y] = c switch
                {
                    '.' => TileType.Dirt,
                    'X' => TileType.Stone,
                    'A' => TileType.Cobble,
                    'B' => TileType.Bedrock,
                    'C' => TileType.Iron,
                    'D' => TileType.Osmium,
                    'E' => TileType.Base,
                    'F' => TileType.Acid,
                    '?' => TileType.Unknown,
                    '0' => TileType.Robot0,
                    '1' => TileType.Robot1,
                    '2' => TileType.Robot2,
                    '3' => TileType.Robot3,
                    '4' => TileType.Robot4,
                    _ => throw new Exception($"Invalid tile {c}")
                };
            }
        }

        lines = lines[size.Y..];

        var pos = PopTuple(ref lines);

        var statsTokens = lines[0].Split(' ');
        var inventoryTokens = lines[1].Split(' ');

        state.Player = new Player
        {
            Position = pos,
            Hp = int.Parse(statsTokens[0]),
            Dig = int.Parse(statsTokens[1]),
            Attack = int.Parse(statsTokens[2]),
            Movement = int.Parse(statsTokens[3]),
            Sight = int.Parse(statsTokens[4]),
            HasAntenna = int.Parse(statsTokens[5]) == 1,
            HasBattery = int.Parse(statsTokens[6]) == 1,
            CobbleCount = int.Parse(inventoryTokens[0]),
            IronCount = int.Parse(inventoryTokens[1]),
            OsmiumCount = int.Parse(inventoryTokens[2])
        };

        state.ScanView();

        return state;
    }

    #endregion
}

public static class Tiles
{
    public static bool IsRobot(this TileType type) => type is TileType.Robot0 or TileType.Robot1 or TileType.Robot2 or TileType.Robot3 or TileType.Robot4;

    public static bool IsUnbreakable(this TileType type) => type is TileType.Bedrock;

    public static bool IsWalkable(this TileType type) => type is TileType.Dirt or TileType.Base or TileType.Acid or TileType.Unknown;

    public static TileType ParseTile(char c) => c switch
    {
        'X' => TileType.Stone,
        'A' => TileType.Cobble,
        'B' => TileType.Bedrock,
        'C' => TileType.Iron,
        'D' => TileType.Osmium,
        'E' => TileType.Base,
        'F' => TileType.Acid,
        '.' => TileType.Dirt,
        '0' => TileType.Robot0,
        '1' => TileType.Robot1,
        '2' => TileType.Robot2,
        '3' => TileType.Robot3,
        '4' => TileType.Robot4,
        _ => TileType.Unknown
    };

    public static char Char(this TileType type) => type switch
    {
        TileType.Stone => 'X',
        TileType.Cobble => 'A',
        TileType.Bedrock => 'B',
        TileType.Iron => 'C',
        TileType.Osmium => 'D',
        TileType.Base => 'E',
        TileType.Acid => 'F',
        TileType.Dirt => '.',
        TileType.Unknown => '?',
        TileType.Robot0 => '0',
        TileType.Robot1 => '1',
        TileType.Robot2 => '2',
        TileType.Robot3 => '3',
        TileType.Robot4 => '4',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
