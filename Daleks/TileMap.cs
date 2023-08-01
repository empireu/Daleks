using System.Diagnostics;
using Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vector2di = Common.Vector2di;

namespace Daleks;

public sealed class TileMap : IReadOnlyGrid<TileType>
{
    private readonly float _diagonalPenalty;
    private readonly Grid<PathfindingCell> _pathfindingGrid;
    private readonly float[] _costMap;

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

            if (cost < 0)
            {
                throw new Exception($"Invalid cost {cost}");
            }
        }

        if (diagonalPenalty < 0)
        {
            throw new Exception($"Invalid diagonal penalty {diagonalPenalty}");
        }

        Size = size;
        Tiles = new Grid<TileType>(size);

        Array.Fill(Tiles.Storage, TileType.Unknown);

        _pathfindingGrid = new Grid<PathfindingCell>(size);

        ClearPathfindingData();
    }
    
    public Vector2di Size { get; }

    public Grid<TileType> Tiles { get; }
    
    private void ClearPathfindingData()
    {
        Array.Fill(_pathfindingGrid.Storage, new PathfindingCell
        {
            Ancestor = null,
            Cost = float.MaxValue,
            IsVisited = false,
        });
    }

    private CachedPath? _cachedPath;

    public bool CanAccess(Vector2di startPoint, Vector2di goalPoint)
    {
        if (Tiles[startPoint].IsUnbreakable() || Tiles[goalPoint].IsUnbreakable())
        {
            return false;
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

        return false;
    }

    public bool TryFindPath(Vector2di startPoint, Vector2di goalPoint, [NotNullWhen(true)] out List<Vector2di>? path)
    {
        CanAccess(startPoint, goalPoint);

        void EvictCache()
        {
            _cachedPath = null;
        }

        if (_cachedPath != null && _cachedPath.Goal == goalPoint)
        {
            var queue = new Queue<Vector2di>(_cachedPath.Path);
            var cache = _cachedPath.Path;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < cache.Count; i++)
            {
                if (Tiles[cache[i]].IsUnbreakable())
                {
                    goto pass;
                }
            }

            while (queue.Count > 0)
            {
                var currentPoint = queue.Dequeue();

                if (currentPoint == startPoint)
                {
                    path = new List<Vector2di>(queue.Count + 1) { startPoint };

                    path.AddRange(queue);

                    if (path.Count == 1)
                    {
                        path.Add(goalPoint);
                    }

                    return true;
                }
            }

            pass:
            EvictCache();
        }

        var successful = FindPathCore(startPoint, goalPoint, out path);
        
        ClearPathfindingData();

        if (successful)
        {
            _cachedPath = new CachedPath(goalPoint, path!)
            {
                VehiclePos = startPoint
            };
        }

        return successful;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float DiagonalCost(Vector2di cPos)
    {
        var c = _pathfindingGrid[cPos];
        if (!c.Ancestor.HasValue) return 0f;

        var bPos = c.Ancestor.Value;

        var b = _pathfindingGrid[bPos];
        if (!b.Ancestor.HasValue) return 0f;

        var aPos = b.Ancestor.Value;

        var df = aPos - cPos;

        if (df.X != 0 && df.Y != 0)
        {
            return _diagonalPenalty;
        }

        return 0f;
    }

    private bool FindPathCore(Vector2di startPoint, Vector2di goalPoint, [NotNullWhen(true)] out List<Vector2di>? path)
    {
        if (!Tiles.IsWithinBounds(startPoint) || !Tiles.IsWithinBounds(goalPoint))
        {
            path = null;
            return false;
        }

        if (startPoint == goalPoint)
        {
            path = new List<Vector2di> { startPoint, goalPoint };
            return true;
        }

        var queue = new PrioritySet<Vector2di, float>();
        
        queue.Enqueue(startPoint, 0);
     
        _pathfindingGrid[startPoint].Cost = 0;

        while (queue.Count > 0)
        {
            var currentPoint = queue.Dequeue();

            if (currentPoint == goalPoint)
            {
                path = new List<Vector2di>();

                while (true)
                {
                    path.Add(goalPoint);

                    goalPoint = _pathfindingGrid[goalPoint].Ancestor ?? throw new Exception("Expected ancestor");

                    if (goalPoint == startPoint)
                    {
                        path.Add(startPoint);

                        break;
                    }
                }

                path.Reverse();

                return true;
            }

            var currentCell = _pathfindingGrid[currentPoint];

            _pathfindingGrid[currentPoint].IsVisited = true;

            for (var i = 0; i < 4; i++)
            {
                var neighborPoint = currentPoint + (Direction)i;

                if (!IsWithinBounds(neighborPoint))
                {
                    continue;
                }

                ref var neighborCell = ref _pathfindingGrid[neighborPoint];

                if (neighborCell.IsVisited)
                {
                    continue;
                }

                var type = Tiles[neighborPoint];

                if (type.IsUnbreakable())
                {
                    continue;
                }

                var newCost = currentCell.Cost + DiagonalCost(neighborPoint) + _costMap[(int)type] + 1f;

                if (newCost < neighborCell.Cost)
                {
                    neighborCell.Cost = newCost;
                    neighborCell.Ancestor = currentPoint;
                }

                queue.EnqueueOrUpdate(neighborPoint, neighborCell.Cost);
            }
        }

        path = null;

        return false;
    }

    public TileType this[int x, int y] => Tiles[x, y];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(int x, int y) => Tiles.IsWithinBounds(x, y);
  
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(Vector2di v) => Tiles.IsWithinBounds(v);

    public struct PathfindingCell : IComparable<PathfindingCell>
    {
        public Vector2di? Ancestor;
        public float Cost;
        public bool IsVisited;

        public readonly int CompareTo(PathfindingCell other) => Cost.CompareTo(other.Cost);
    }

    private sealed class CachedPath
    {
        public Vector2di Goal { get; }

        public List<Vector2di> Path { get; }

        public CachedPath(Vector2di goal, List<Vector2di> path)
        {
            Goal = goal;
            Path = path;
        }

        public Vector2di VehiclePos { get; set; }
    }
}