using System.Text;
using Common;

namespace Common
{
}

namespace Daleks
{
    public readonly struct PlayerState
    {
        public Vector2di Position { get; init; }
        public int Hp { get; init; }
        public int Dig { get; init; }
        public int Attack { get; init; }
        public int Movement { get; init; }
        public int Vision { get; init; }
        public bool HasAntenna { get; init; }
        public bool HasBattery { get; init; }
        public int CobbleCount { get; init; }
        public int IronCount { get; init; }
        public int OsmiumCount { get; init; }
    }

    public class GameState
    {
        public int Round { get; }

        private readonly Grid<TileType> _grid;

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

        public PlayerState Player { get; private set; }

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

            state.Player = new PlayerState
            {
                Position = pos,
                Hp = int.Parse(statsTokens[0]),
                Dig = int.Parse(statsTokens[1]),
                Attack = int.Parse(statsTokens[2]),
                Movement = int.Parse(statsTokens[3]),
                Vision = int.Parse(statsTokens[4]),
                HasAntenna = int.Parse(statsTokens[5]) == 1,
                HasBattery = int.Parse(statsTokens[6]) == 1,
                CobbleCount = int.Parse(inventoryTokens[0]),
                IronCount = int.Parse(inventoryTokens[1]),
                OsmiumCount = int.Parse(inventoryTokens[2])
            };

            return state;
        }

        public GameState Bind(PlayerState player)
        {
            return new GameState(_grid, Round)
            {
                Player = player
            };
        }

        public TileType Neighbor(Direction dir) => this[Player.Position + dir];
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

    public enum ActionType
    {
        Attack,
        Mine,
        Scan,
        Place
    }

    public enum UpgradeType
    {
        Sight,
        Attack,
        Drill,
        Movement,
        Antenna,
        Battery,
        Heal
    }

    public readonly struct ActionCommand
    {
        public ActionType Type { get; }
        public Direction Dir { get; }

        public ActionCommand(ActionType type, Direction dir)
        {
            Type = type;
            Dir = dir;
        }
    }

    public class CommandState
    {
        public CommandState(GameState headState)
        {
            Head = headState;
            _snapshots = new List<GameState> { Head };
        }

        private readonly List<ActionCommand> _actions = new();
        private readonly List<Direction> _moves = new();
        private readonly List<UpgradeType> _upgrades = new();
        private readonly List<GameState> _snapshots;

        public IReadOnlyList<ActionCommand> Actions => _actions;
        public IReadOnlyList<Direction> Moves => _moves;
        public IReadOnlyList<UpgradeType> Upgrades => _upgrades;
        public IReadOnlyList<GameState> StateSnapshots => _snapshots;
    
        public GameState Head { get; }

        public GameState Tail => _snapshots.Last();

        public bool HasAction => _actions.Any();

        private void ValidateAction()
        {
            if (HasAction)
            {
                throw new Exception("Only one action is allowed per round");
            }
        }

        public int RemainingMoves => Head.Player.Movement - _moves.Count;
    
        public bool CanMove => RemainingMoves > 0;

        public bool Move(Direction dir)
        {
            if (!CanMove)
            {
                return false;
            }

            PushState(Tail.Bind(Tail.Player with
            {
                Position = Tail.Player.Position + dir
            }));

            _moves.Add(dir);

            return true;
        }

        public int MineCount => Head.Player.Dig;

        public void Mine(IEnumerable<Direction> directions)
        {
            ValidateAction();

            foreach (var direction in directions)
            {
                _actions.Add(new ActionCommand(ActionType.Mine, direction));
            }
        }

        public void Attack(Direction dir)
        {
            ValidateAction();

            _actions.Add(new ActionCommand(ActionType.Attack, dir));
        }

        public void Mine(Direction direction) => Mine(new[] { direction });

        public bool CanHeal => Tail.Player is { OsmiumCount: > 0, Hp: < 15 };
        public bool CanBuyBattery => Tail.Player is { OsmiumCount: > 0, IronCount: > 0 };

        public bool BuyBattery()
        {
            if (Tail.Player.HasBattery)
            {
                return true;
            }

            if (!CanBuyBattery)
            {
                return false;
            }

            PushState(Tail.Bind(Tail.Player with
            {
                HasBattery = true,
                OsmiumCount = Tail.Player.OsmiumCount - 1,
                IronCount = Tail.Player.IronCount - 1
            }));

            _upgrades.Add(UpgradeType.Battery);

            return true;
        }

        public bool BuyAttack()
        {
            if (Tail.Player.Attack == 3)
            {
                return false;
            }

            if (Tail.Player is { Attack: 1, IronCount: >= 3 })
            {
                PushState(Tail.Bind(Tail.Player with
                {
                    Attack = 2,
                    IronCount = Tail.Player.IronCount - 3
                }));
                _upgrades.Add(UpgradeType.Attack);
                return true;
            }

            if (Tail.Player is { Attack: 2, IronCount: >= 6, OsmiumCount: >= 1 })
            {
                PushState(Tail.Bind(Tail.Player with
                {
                    Attack = 3,
                    IronCount = Tail.Player.IronCount - 6,
                    OsmiumCount = Tail.Player.OsmiumCount - 1
                }));
                _upgrades.Add(UpgradeType.Attack);
                return true;
            }

            return false;
        }

        public bool BuySight()
        {
            if (Tail.Player.Vision == 3)
            {
                return false;
            }

            if (Tail.Player is { Vision: 1, IronCount: >= 3 })
            {
                PushState(Tail.Bind(Tail.Player with
                {
                    Vision = 2,
                    IronCount = Tail.Player.IronCount - 3
                }));
                _upgrades.Add(UpgradeType.Sight);
                return true;
            }

            if (Tail.Player is { Vision: 2, IronCount: >= 6, OsmiumCount: >= 1 })
            {
                PushState(Tail.Bind(Tail.Player with
                {
                    Vision = 3,
                    IronCount = Tail.Player.IronCount - 6,
                    OsmiumCount = Tail.Player.OsmiumCount - 1
                }));
                _upgrades.Add(UpgradeType.Sight);
                return true;
            }

            return false;
        }

        private void PushState(GameState state) => _snapshots.Add(state);

        public bool Heal()
        {
            if (!CanHeal)
            {
                return false;
            }

            PushState(Tail.Bind(Tail.Player with
            {
                Hp = Math.Min(Tail.Player.Hp + 5, 15),
                OsmiumCount = Tail.Player.OsmiumCount - 1
            }));

            _upgrades.Add(UpgradeType.Heal);

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
                    UpgradeType.Sight => "S",
                    UpgradeType.Attack => "A",
                    UpgradeType.Drill => "D",
                    UpgradeType.Movement => "M",
                    UpgradeType.Antenna => "R",
                    UpgradeType.Battery => "B",
                    UpgradeType.Heal => "H",
                    _ => throw new ArgumentOutOfRangeException()
                });
            }

            return sb.ToString();
        }
    }
}