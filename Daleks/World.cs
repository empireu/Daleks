using System.Diagnostics;
using Common;
using System.Runtime.CompilerServices;

namespace Daleks;

public sealed class TileMap : IReadOnlyGrid<TileType>
{
    private readonly float _diagonalPenalty;
    private readonly float[] _costMap;

    private sealed class CacheData
    {
        /// <summary>
        ///     Stores data obtained after running Dijkstra
        /// </summary>
        public sealed class PathfindingInfo
        {
            public Vector2ds Start { get; }
            public Grid<PathfindingCell> Grid { get; }

            // End point -> path
            // If null, the path is blocked off
            public readonly Dictionary<Vector2ds, List<Vector2ds>?> TracedPaths = new();

            public PathfindingInfo(Vector2ds start, Grid<PathfindingCell> grid)
            {
                Start = start;
                Grid = grid;
            }
        }

        // Start point -> Dijkstra grid
        public readonly Dictionary<Vector2ds, PathfindingInfo> Paths = new();
        public readonly Dictionary<(Vector2ds, Vector2ds), bool> CanAccess = new();

        public void Clear()
        {
            Paths.Clear();
        }
    }

    private readonly CacheData _cache = new();

    public IReadOnlyList<TileType> Cells => Tiles.Cells;

    public Grid<float> CostOverride { get; }

    public TileMap(Vector2ds size, IReadOnlyDictionary<TileType, float> costMap, float diagonalPenalty)
    {
        _diagonalPenalty = diagonalPenalty;
     
        _costMap = Enum
            .GetValues<TileType>()
            .OrderBy(x => (int)x)
            .Select(type => costMap.TryGetValue(type, out var cost) ? cost : 0f)
            .ToArray();

        foreach (var (t, cost) in costMap)
        {
            if (!_costMap[(int)t].Equals(cost))
            {
                throw new Exception("Validation failed");
            }
        }

        var minCost = costMap.Values.Min();

        if (minCost < 0)
        {
            // Normalize costs:
            var n = Math.Abs(minCost);

            foreach (var tile in costMap.Keys)
            {
                _costMap[(int)tile] += n;
            }
        }

        if (_costMap.Any(c => c < 0))
        {
            throw new Exception("Validation failed");
        }

        if (diagonalPenalty < 0)
        {
            throw new Exception($"Invalid diagonal penalty {diagonalPenalty}");
        }

        Size = size;
        Tiles = new Grid<TileType>(size);

        Array.Fill(Tiles.Storage, TileType.Unknown);

        CostOverride = new Grid<float>(Size);
    }

    public void ClearCaches()
    {
        _cache.Clear();
        Array.Fill(CostOverride.Storage, 0);
    }

    public Vector2ds Size { get; }

    public Grid<TileType> Tiles { get; }
    
    private Grid<PathfindingCell> CreateGrid(Vector2ds startPoint)
    {
        var grid = new Grid<PathfindingCell>(Size);

        Array.Fill(grid.Storage, new PathfindingCell
        {
            Ancestor = null,
            Cost = float.MaxValue,
        });

        grid[startPoint].Cost = 0;

        return grid;
    }

    public bool CanAccess(Vector2ds startPoint, Vector2ds goalPoint)
    {
        if (Tiles[startPoint].IsUnbreakable() || Tiles[goalPoint].IsUnbreakable())
        {
            return false;
        }

        var pair = (startPoint, goalPoint).Map(p => p.goalPoint.NormSqr < p.startPoint.NormSqr ? (p.goalPoint, p.startPoint) : p);

        if (_cache.CanAccess.TryGetValue(pair, out var result))
        {
            return result;
        }

        if (_cache.Paths.TryGetValue(startPoint, out var info))
        {
            if (info.TracedPaths.TryGetValue(goalPoint, out var p))
            {
                return p != null;
            }
        }

        var queue = new PriorityQueue<Vector2ds, float>();

        float Heuristic(Vector2ds p) => Vector2ds.Manhattan(p, goalPoint);

        queue.Enqueue(startPoint, Heuristic(startPoint));

        var visited = new HashSet<Vector2ds>();

        while (queue.Count > 0)
        {
            var front = queue.Dequeue();

            if (front == goalPoint)
            {
                _cache.CanAccess.Add(pair, true);
                return true;
            }

            if (!visited.Add(front))
            {
                continue;
            }

            for (var i = 0; i < 4; i++)
            {
                var neighbor = front + (Direction)i;

                if (Tiles.IsWithinBounds(neighbor) && !Tiles[neighbor].IsUnbreakable())
                {
                    queue.Enqueue(neighbor, Heuristic(neighbor));
                }
            }
        }

        _cache.CanAccess.Add(pair, false);

        return false;
    }

    private List<Vector2ds>? Trace(CacheData.PathfindingInfo info, Vector2ds goal)
    {
        var result = new List<Vector2ds>();

        while (true)
        {
            Debug.Assert(!Tiles[goal].IsUnbreakable());

            result.Add(goal);

            var ancestor = info.Grid[goal].Ancestor;

            if (ancestor == null || Tiles[ancestor.Value].IsUnbreakable())
            {
                return null;
            }

            goal = ancestor.Value;

            if (goal == info.Start)
            {
                result.Add(info.Start);
                break;
            }
        }

        result.Reverse();

        return result;
    }

    private List<Vector2ds>? FromExistingGrid(Vector2ds goalPoint, CacheData.PathfindingInfo info)
    {
        if (info.TracedPaths.TryGetValue(goalPoint, out var path))
        {
            return path;
        }

        path = Trace(info, goalPoint);

        info.TracedPaths.Add(goalPoint, path);

        return path;
    }

    public IReadOnlyList<Vector2ds>? FindPath(Vector2ds startPoint, Vector2ds goalPoint)
    {
        if (startPoint == goalPoint)
        {
            return new List<Vector2ds> { startPoint, goalPoint };
        }
        
        if (!Tiles.IsWithinBounds(startPoint) || Tiles[startPoint].IsUnbreakable())
        {
            return null;
        }

        if (!Tiles.IsWithinBounds(goalPoint) || Tiles[goalPoint].IsUnbreakable())
        {
            return null;
        }

        if (_cache.Paths.TryGetValue(startPoint, out var existingData))
        {
            return FromExistingGrid(goalPoint, existingData);
        }

        var grid = RunPathfinding(startPoint);

        existingData = new CacheData.PathfindingInfo(startPoint, grid);

        _cache.Paths.Add(startPoint, existingData);

        return FromExistingGrid(goalPoint, existingData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float DiagonalCost(Vector2ds cPos, Grid<PathfindingCell> grid)
    {
        var c = grid[cPos];
        if (!c.Ancestor.HasValue) return 0f;

        var bPos = c.Ancestor.Value;

        var b = grid[bPos];
        if (!b.Ancestor.HasValue) return 0f;

        var aPos = b.Ancestor.Value;

        var df = aPos - cPos;

        if (df.X != 0 && df.Y != 0)
        {
            return _diagonalPenalty;
        }

        return 0f;
    }

    private Grid<PathfindingCell> RunPathfinding(Vector2ds startPoint)
    {
        var grid = CreateGrid(startPoint);

        var queue = new PrioritySet<Vector2ds, float>(Size.X * Size.Y, null, null);
        
        for (var y = 0; y < grid.Size.Y; y++)
        {
            for (var x = 0; x < grid.Size.X; x++)
            {
                queue.Enqueue(new Vector2ds(x, y), grid[x, y].Cost);
            }
        }

        while (queue.Count > 0)
        {
            var currentPoint = queue.Dequeue();

            var currentCell = grid[currentPoint];

            for (var i = 0; i < 4; i++)
            {
                var neighborPoint = currentPoint + (Direction)i;

                if (!IsWithinBounds(neighborPoint))
                {
                    continue;
                }

                ref var neighborCell = ref grid[neighborPoint];

                var neighborType = Tiles[neighborPoint];

                if (neighborType.IsUnbreakable())
                {
                    continue;
                }

                var newCost = 
                    currentCell.Cost + 
                    DiagonalCost(neighborPoint, grid) + 
                    _costMap[(int)neighborType] + 
                    CostOverride[neighborPoint] +
                    1f;

                if (newCost < neighborCell.Cost)
                {
                    neighborCell.Cost = newCost;
                    neighborCell.Ancestor = currentPoint;
                    queue.Update(neighborPoint, newCost);
                }
            }
        }

        return grid;
    }

    public TileType this[int x, int y] => Tiles[x, y];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(int x, int y) => Tiles.IsWithinBounds(x, y);
  
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(Vector2ds v) => Tiles.IsWithinBounds(v);

    public struct PathfindingCell : IComparable<PathfindingCell>
    {
        public Vector2ds? Ancestor;
        
        public float Cost;

        public readonly int CompareTo(PathfindingCell other) => Cost.CompareTo(other.Cost);
    }
}

public sealed class ExplorationAnalyzer
{
    private readonly TileMap _map;

    private readonly HashSet<Vector2ds> _frontierEdges = new();

    public ExplorationAnalyzer(TileMap map)
    {
        _map = map;
    }

    public IReadOnlySet<Vector2ds> FrontierEdges => _frontierEdges;

    public void UpdateFrontiers(Options options)
    {
        _frontierEdges.Clear();
        
        var tiles = _map.Tiles;

        for (var y = 0; y < tiles.Size.Y; y++)
        {
            for (var x = 0; x < tiles.Size.X; x++)
            {
                if (_map[x, y] != TileType.Dirt)
                {
                    continue;
                }

                if (tiles.IsWithinBounds(x - 1, y) && tiles[x - 1, y] == TileType.Unknown)
                {
                    _frontierEdges.Add(new Vector2ds(x, y));
                    continue;
                }

                if (tiles.IsWithinBounds(x + 1, y) && tiles[x + 1 , y] == TileType.Unknown)
                {
                    _frontierEdges.Add(new Vector2ds(x, y));
                    continue;
                }
                
                if (tiles.IsWithinBounds(x, y - 1) && tiles[x, y - 1] == TileType.Unknown)
                {
                    _frontierEdges.Add(new Vector2ds(x, y));
                    continue;
                }

                if (tiles.IsWithinBounds(x, y + 1) && tiles[x, y + 1] == TileType.Unknown)
                {
                    _frontierEdges.Add(new Vector2ds(x, y));
                    continue;
                }
            }
        }

        _frontierEdges.RemoveWhere(frontier => !_map.CanAccess(options.PlayerPosition, frontier));
    }

    // Cost metric for visiting a frontier.
    // This can factor in energy expenditure (fuel), time, etc.
    private double ImplicitCost(ref Options options, Vector2ds frontierPoint)
    {
        var playerDistance = Vector2ds.Distance(frontierPoint, options.PlayerPosition) / options.MovementSpeed;
        var baseDistance = Vector2ds.Distance(frontierPoint, options.BasePosition) / options.MovementSpeed;

        return options.KCostPlayer * playerDistance + options.KCostBase * baseDistance;
    }

    // Utility (gain) of reaching a frontier (how useful this frontier is, compared to others)
    // Currently, the number of cells discovered is used as utility.
    private double Utility(ref Options options, Vector2ds frontierPoint)
    {
        var kJ = options.VisionOffsets.Count(offset =>
        {
            var position = frontierPoint + offset;

            return _map.IsWithinBounds(position) && _map.Tiles[position] == TileType.Unknown;
        });

        return options.KUtility * kJ;
    }

    public Vector2ds? GetExplorationTarget(Options options)
    {
        if (_frontierEdges.Count == 0)
        {
            return null;
        }

        var bestFrontier = Vector2ds.Zero;
        var bestWeight = double.MaxValue;

        foreach (var frontier in _frontierEdges)
        {
            var implicitCost = ImplicitCost(ref options, frontier);
            var utility = Utility(ref options, frontier);
            var weight = implicitCost - utility;

            if (weight < bestWeight)
            {
                bestWeight = weight;
                bestFrontier = frontier;
            }
        }

        return bestFrontier;
    }

    public readonly struct Options
    {
        public required double KCostPlayer { get; init; }
        public required double KCostBase { get; init; }
        public required double KUtility { get; init; }
        public required Vector2ds PlayerPosition { get; init; }
        public required Vector2ds BasePosition { get; init; }
        public required Vector2ds[] VisionOffsets { get; init; }
        public required double MovementSpeed { get; init; }
    }
}