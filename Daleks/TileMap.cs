using System.Diagnostics;
using Common;
using System.Runtime.CompilerServices;
using Vector2di = Common.Vector2di;

namespace Daleks;

public sealed class TileMap : IReadOnlyGrid<TileType>
{
    private readonly float _diagonalPenalty;
    private readonly float[] _costMap;

    private bool _begun;

    private sealed class FrameData
    {
        /// <summary>
        ///     Stores data obtained after running Dijkstra
        /// </summary>
        public sealed class PathfindingInfo
        {
            public Vector2di Start { get; }
            public Grid<PathfindingCell> Grid { get; }

            // End point -> path
            // If null, the path is blocked off
            public readonly Dictionary<Vector2di, List<Vector2di>?> TracedPaths = new();

            public PathfindingInfo(Vector2di start, Grid<PathfindingCell> grid)
            {
                Start = start;
                Grid = grid;
            }
        }

        // Start point -> Dijkstra grid
        public readonly Dictionary<Vector2di, PathfindingInfo> Paths = new();
        public readonly Dictionary<(Vector2di, Vector2di), bool> CanAccess = new();

        public void Clear()
        {
            Paths.Clear();
        }
    }

    private readonly FrameData _data = new();

    public Grid<float> CostOverride { get; }

    private readonly List<HashSet<Vector2di>> _unexploredClusters = new();

    public IReadOnlyList<IReadOnlySet<Vector2di>> UnexploredClusters => _unexploredClusters;

    public TileMap(Vector2di size, IReadOnlyDictionary<TileType, float> costMap, float diagonalPenalty)
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

    private void ScanUnexploredClusters()
    {
        _unexploredClusters.Clear();

        var unexplored = new HashSet<Vector2di>();

        for (var y = 1; y < Size.Y - 1; y++)
        {
            for (var x = 1; x < Size.X - 1; x++)
            {
                var tile = new Vector2di(x, y);

                if (Tiles[tile] == TileType.Unknown)
                {
                    unexplored.Add(tile);
                }
            }
        }

        var queue = new Queue<Vector2di>();

        while (unexplored.Count > 0)
        {
            queue.Enqueue(unexplored.Last());

            var cluster = new HashSet<Vector2di>();

            while (queue.Count > 0)
            {
                var front = queue.Dequeue();

                if (!cluster.Add(front))
                {
                    continue;
                }

                unexplored.Remove(front);

                for (byte i = 0; i < 4; i++)
                {
                    var neighbor = front + (Direction)i;

                    if (Tiles.IsWithinBounds(neighbor) && Tiles[neighbor] == TileType.Unknown)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            _unexploredClusters.Add(cluster);
        }
    }

    public void BeginFrame()
    {
        if (_begun)
        {
            throw new InvalidOperationException("Cannot begin frame before ending previous frame");
        }

        _begun = true;

        _data.Clear();
        ScanUnexploredClusters();
    }

    public void EndFrame()
    {
        if (!_begun)
        {
            throw new InvalidOperationException("Cannot end frame before beginning");
        }

        _begun = false;
    }

    private void Validate()
    {
        if (!_begun)
        {
            throw new InvalidOperationException("Never begun frame");
        }
    }

    public Vector2di Size { get; }

    public Grid<TileType> Tiles { get; }
    
    private Grid<PathfindingCell> CreateGrid(Vector2di startPoint)
    {
        var grid = new Grid<PathfindingCell>(Size);

        Array.Fill(grid.Storage, new PathfindingCell
        {
            Ancestor = null,
            Cost = float.MaxValue,
            IsVisited = false,
        });

        grid[startPoint].Cost = 0;

        return grid;
    }

    public bool CanAccess(Vector2di startPoint, Vector2di goalPoint)
    {
        Validate();

        if (Tiles[startPoint].IsUnbreakable() || Tiles[goalPoint].IsUnbreakable())
        {
            return false;
        }

        var pair = (startPoint, goalPoint).Map(p => p.goalPoint.NormSqr < p.startPoint.NormSqr ? (p.goalPoint, p.startPoint) : p);

        if (_data.CanAccess.TryGetValue(pair, out var result))
        {
            return result;
        }

        if (_data.Paths.TryGetValue(startPoint, out var info))
        {
            if (info.TracedPaths.TryGetValue(goalPoint, out var p))
            {
                return p != null;
            }
        }

        var queue = new PriorityQueue<Vector2di, float>();

        float Heuristic(Vector2di p) => Vector2di.Manhattan(p, goalPoint);

        queue.Enqueue(startPoint, Heuristic(startPoint));

        var visited = new HashSet<Vector2di>();

        while (queue.Count > 0)
        {
            var front = queue.Dequeue();

            if (front == goalPoint)
            {
                _data.CanAccess.Add(pair, true);
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

        _data.CanAccess.Add(pair, false);

        return false;
    }

    private List<Vector2di>? Trace(FrameData.PathfindingInfo info, Vector2di goal)
    {
        var result = new List<Vector2di>();

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

    private List<Vector2di>? FromExistingGrid(Vector2di goalPoint, FrameData.PathfindingInfo info)
    {
        if (info.TracedPaths.TryGetValue(goalPoint, out var path))
        {
            return path;
        }

        path = Trace(info, goalPoint);

        info.TracedPaths.Add(goalPoint, path);

        return path;
    }

    public IReadOnlyList<Vector2di>? FindPath(Vector2di startPoint, Vector2di goalPoint)
    {
        Validate();

        if (startPoint == goalPoint)
        {
            return new List<Vector2di> { startPoint, goalPoint };
        }
        
        if (!Tiles.IsWithinBounds(startPoint) || Tiles[startPoint].IsUnbreakable())
        {
            return null;
        }

        if (!Tiles.IsWithinBounds(goalPoint) || Tiles[goalPoint].IsUnbreakable())
        {
            return null;
        }

        if (_data.Paths.TryGetValue(startPoint, out var existingData))
        {
            return FromExistingGrid(goalPoint, existingData);
        }

        var grid = RunPathfinding(startPoint);

        existingData = new FrameData.PathfindingInfo(startPoint, grid);

        _data.Paths.Add(startPoint, existingData);

        return FromExistingGrid(goalPoint, existingData);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float DiagonalCost(Vector2di cPos, Grid<PathfindingCell> grid)
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

    private Grid<PathfindingCell> RunPathfinding(Vector2di startPoint)
    {
        var queue = new PrioritySet<Vector2di, float>();
        
        queue.Enqueue(startPoint, 0);

        var grid = CreateGrid(startPoint);

        while (queue.Count > 0)
        {
            var currentPoint = queue.Dequeue();

            var currentCell = grid[currentPoint];

            grid[currentPoint].IsVisited = true;

            for (var i = 0; i < 4; i++)
            {
                var neighborPoint = currentPoint + (Direction)i;

                if (!IsWithinBounds(neighborPoint))
                {
                    continue;
                }

                ref var neighborCell = ref grid[neighborPoint];

                if (neighborCell.IsVisited)
                {
                    continue;
                }

                var type = Tiles[neighborPoint];

                if (type.IsUnbreakable())
                {
                    continue;
                }

                var newCost = 
                    currentCell.Cost + 
                    DiagonalCost(neighborPoint, grid) + 
                    _costMap[(int)type] + 
                    CostOverride[neighborPoint] +
                    1f;

                if (newCost < neighborCell.Cost)
                {
                    neighborCell.Cost = newCost;
                    neighborCell.Ancestor = currentPoint;
                }

                queue.EnqueueOrUpdate(neighborPoint, neighborCell.Cost);
            }
        }

        return grid;
    }

    public TileType this[int x, int y] => Tiles[x, y];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(int x, int y) => Tiles.IsWithinBounds(x, y);
  
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(Vector2di v) => Tiles.IsWithinBounds(v);

    private struct PathfindingCell : IComparable<PathfindingCell>
    {
        public Vector2di? Ancestor;
        public float Cost;
        public bool IsVisited;

        public readonly int CompareTo(PathfindingCell other) => Cost.CompareTo(other.Cost);
    }
}