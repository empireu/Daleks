﻿using System.Text;

namespace Common;

public readonly struct Player
{
    public Vector2di Position { get; init; }
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
        AbilityType.Dig => Dig,
        AbilityType.Attack => Attack,
        AbilityType.Sight => Sight,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public Player WithAbilityLevel(AbilityType type, int level) => type switch
    {
        AbilityType.Movement => this with { Movement = level },
        AbilityType.Dig => this with { Dig = level },
        AbilityType.Attack => this with { Attack = level },
        AbilityType.Sight => this with { Sight = level },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}

public sealed class CommandState
{
    public Vector2di BasePosition { get; }

    public CommandState(GameState headState, Vector2di basePosition)
    {
        BasePosition = basePosition;
        _states = new List<GameState> { headState };
    }

    private readonly List<ActionCommand> _actions = new();
    private readonly List<Direction> _moves = new();
    private readonly List<BuyType> _upgrades = new();
    private readonly List<GameState> _states;

    public IReadOnlyList<ActionCommand> Actions => _actions;
    public IReadOnlyList<Direction> Moves => _moves;
    public IReadOnlyList<BuyType> Upgrades => _upgrades;
    public IReadOnlyList<GameState> States => _states;

    public GameState Head => _states.First();

    public GameState Tail => _states.Last();

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
    
    public bool Mine(Direction direction)
    {
        if (_actions.Any(a => a.Type != ActionType.Mine))
        {
            throw new Exception($"Cannot use mine action over {string.Join(", ", Actions)}");
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

    public void Attack(Direction dir)
    {
        ValidateExclusiveAction();

        _actions.Add(new ActionCommand(ActionType.Attack, dir));
    }

    // Tail because the inventory may be modified by other things
    public bool CouldHeal => Tail.Player is { OsmiumCount: > 0, Hp: < 15 };
    public bool CouldBuyBattery => Tail.Player is { IronCount: >= 1, OsmiumCount: >= 1 };
    public bool CouldBuyAntenna => Tail.Player is { IronCount: >= 2, OsmiumCount: >= 1 };

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
            AbilityType.Dig => BuyType.Drill,
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

    public bool BuyAttack()
    {
        if (Tail.Player.Attack == 3)
        {
            return false;
        }

        if (Tail.Player is { Attack: 1, IronCount: >= 3 })
        {
            PushState(p => p with
            {
                Attack = 2,
                IronCount = Tail.Player.IronCount - 3
            });

            _upgrades.Add(BuyType.Attack);

            return true;
        }

        if (Tail.Player is { Attack: 2, IronCount: >= 6, OsmiumCount: >= 1 })
        {
            PushState(p => p with
            {
                Attack = 3,
                IronCount = Tail.Player.IronCount - 6,
                OsmiumCount = Tail.Player.OsmiumCount - 1
            });

            _upgrades.Add(BuyType.Attack);
            
            return true;
        }

        return false;
    }

    public bool BuySight()
    {
        if (Tail.Player.Sight == 3)
        {
            return false;
        }

        if (Tail.Player is { Sight: 1, IronCount: >= 3 })
        {
            PushState(Tail.Bind(Tail.Player with
            {
                Sight = 2,
                IronCount = Tail.Player.IronCount - 3
            }));
            _upgrades.Add(BuyType.Sight);
            return true;
        }

        if (Tail.Player is { Sight: 2, IronCount: >= 6, OsmiumCount: >= 1 })
        {
            PushState(Tail.Bind(Tail.Player with
            {
                Sight = 3,
                IronCount = Tail.Player.IronCount - 6,
                OsmiumCount = Tail.Player.OsmiumCount - 1
            }));
            _upgrades.Add(BuyType.Sight);
            return true;
        }

        return false;
    }

    private void PushState(GameState state) => _states.Add(state);

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

public enum AbilityType
{
    Movement,
    Dig,
    Attack,
    Sight,
}

public enum BuyType
{
    Sight,
    Attack,
    Drill,
    Movement,
    Antenna,
    Battery,
    Heal
}

public enum ActionType
{
    Attack,
    Mine,
    Scan,
    Place
}

public enum TileType
{
    Dirt,
    Stone,
    Cobblestone,
    Bedrock,
    Iron,
    Osmium,
    Base,
    Acid,
    Unknown,
    Robot
}

public sealed class GameState
{
    public int Round { get; }

    private readonly Grid<TileType> _grid;

    public IReadOnlyGrid<TileType> Grid => _grid;

    public Vector2di GridSize => _grid.Size;

    private GameState(Grid<TileType> grid, int round)
    {
        _grid = grid;
        Round = round;
    }

    private GameState(Vector2di gridSize, int round)
    {
        _grid = new Grid<TileType>(gridSize);
        Round = round;
    }

    public Player Player { get; private set; }

    private static Vector2di PopTuple(ref Span<string> lines)
    {
        var result = lines[0].Map(str =>
        {
            var tokens = str.Split(' ');

            return new Vector2di(int.Parse(tokens[0]), int.Parse(tokens[1]));
        });

        lines = lines[1..];

        return result;
    }

    public TileType this[int x, int y] => _grid[x, y];
    public TileType this[Vector2di v] => _grid[v.X, v.Y];
    public bool IsWithinBounds(int x, int y) => _grid.IsWithinBounds(x, y);
    public bool IsWithinBounds(Vector2di v) => _grid.IsWithinBounds(v);

    public static GameState Load(Span<string> lines, int round)
    {
        var size = PopTuple(ref lines);
        var state = new GameState(size, round);

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
                    'A' => TileType.Cobblestone,
                    'B' => TileType.Bedrock,
                    'C' => TileType.Iron,
                    'D' => TileType.Osmium,
                    'E' => TileType.Base,
                    'F' => TileType.Acid,
                    '?' => TileType.Unknown,
                    _ => char.IsDigit(c) ? TileType.Robot : throw new Exception($"Invalid tile {c}")
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

        return state;
    }

    public GameState Bind(Player player)
    {
        return new GameState(_grid, Round)
        {
            Player = player
        };
    }

    public TileType Neighbor(Direction dir) => this[Player.Position + dir]; // Bedrock will not allow going out of bounds
}