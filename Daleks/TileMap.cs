using Common;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Daleks;

public sealed class TileMap : IReadOnlyGrid<TileType>
{
    private readonly Grid<AStarCell> _pathfindingGrid;
    private readonly Dictionary<TileType, float> _costMap;

    public float DiagonalPenalty { get; set; } = 3;

    public TileMap(Vector2di size, Dictionary<TileType, float> costMap)
    {
        _costMap = costMap;
        Size = size;
        Tiles = new Grid<TileType>(size);

        Array.Fill(Tiles.Storage, TileType.Unknown);

        _pathfindingGrid = new Grid<AStarCell>(size);
        ClearPathfindingData();

        _unexploredTree = new BitQuadTree(Vector2di.Zero, Math.Max(size.X, size.Y));

        // Very slow but we're doing it once

        //for (var y = 1; y < size.Y - 1; y++)
        //{
        //    for (var x = 1; x < size.X - 1; x++)
        //    {
        //        _unexploredTree.Insert(new Vector2di(x, y));
        //    }
        //}
    }
    
    public Vector2di Size { get; private set; }
    public Grid<TileType> Tiles { get; }

    private readonly BitQuadTree _unexploredTree;

    public int UnexploredTreeVersion { get; private set; }

    public IQuadTreeView UnexploredTree => _unexploredTree;

    private void ClearPathfindingData()
    {
        Array.Fill(_pathfindingGrid.Storage, new AStarCell
        {
            GCost = float.MaxValue
        });
    }

    private CachedPath? _cachedPath;

    public bool TryFindPath(Vector2di startPoint, Vector2di goalPoint, [NotNullWhen(true)] out List<Vector2di>? path)
    {
        void EvictCache()
        {
            _cachedPath = null;
        }

        if (_cachedPath != null && _cachedPath.GoalPos == goalPoint)
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

        var successful = TryFindPathCore(startPoint, goalPoint, out path);
        
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
    private float EdgeCost(Vector2di neighbor) => _costMap.TryGetValue(Tiles[neighbor], out var c) ? c : 1f;

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
            return DiagonalPenalty;
        }

        return 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Heuristic(Vector2di currentNode, Vector2di goal) =>
        Vector2di.Manhattan(currentNode, goal);

    private readonly struct Priority : IComparable<Priority>
    {
        public float FCost { get; init; }
        public float HCost { get; init; }

        public int CompareTo(Priority other) => FCost.Equals(other.FCost)
            ? HCost.CompareTo(other.HCost)
            : FCost.CompareTo(other.FCost);
    }

    /*private sealed class PriorityComparer : IComparer<Priority>
    {
        public int Compare(Priority x, Priority y)
        {
            return x.FCost.Equals(y.FCost) 
                ? x.HCost.CompareTo(y.HCost) 
                : x.FCost.CompareTo(y.FCost);
        }
    }*/

    private bool TryFindPathCore(Vector2di startPoint, Vector2di goalPoint, [NotNullWhen(true)] out List<Vector2di>? path)
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

        _pathfindingGrid[startPoint].GCost = 0;

        var queue = new PrioritySet<Vector2di, Priority>();

        queue.Enqueue(startPoint, new Priority
        {
            FCost = Heuristic(startPoint, goalPoint)
        });

        while (queue.TryDequeue(out var currentPoint, out _))
        {
            ref var current = ref _pathfindingGrid[currentPoint];

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

            for (byte i = 0; i < 4; i++)
            {
                var neighborPoint = currentPoint + (Direction)i;

                if (!Tiles.IsWithinBounds(neighborPoint) || Tiles[neighborPoint].IsUnbreakable())
                {
                    continue;
                }

                ref var neighbor = ref _pathfindingGrid[neighborPoint];

                var tentativeGScore = current.GCost + 1;

                if (!(tentativeGScore < neighbor.GCost))
                {
                    continue;
                }

                neighbor.Ancestor = currentPoint;
                neighbor.GCost = tentativeGScore;
                    
                var hCost = Heuristic(neighborPoint, goalPoint);
                
                var fCost = tentativeGScore + hCost + DiagonalCost(neighborPoint);

                if (_costMap.TryGetValue(this[neighborPoint.X, neighborPoint.Y], out var cost))
                {
                    fCost += cost;
                }

                queue.EnqueueOrUpdate(neighborPoint, new Priority
                {
                    FCost = fCost,
                    HCost = hCost
                });
            }
        }
        path = null;

        return false;
    }

    public TileType this[int x, int y] => Tiles[x, y];

    public bool IsWithinBounds(int x, int y) => Tiles.IsWithinBounds(x, y);

    public bool SetExplored(Vector2di v)
    {
        if (_unexploredTree.Remove(v))
        {
            UnexploredTreeVersion++;
            return true;
        }

        return false;
    }

    public IQuadTreeView? GetUnexploredRegion(Vector2di position) => GetUnexploredRegionCore(_unexploredTree, position);

    private IQuadTreeView? GetUnexploredRegionCore(BitQuadTree node, Vector2di position)
    {
        var p2 = new Vector2(position.X, position.Y);

        while (true)
        {
            if (node.IsFilled || node.Size == 1)
            {
                return node;
            }

            var bestChild = node.GetChild(position);

            if (bestChild == null)
            {
                var bestCost = float.MaxValue;

                for (byte i = 0; i < 4; i++)
                {
                    var child = node.GetChild((BitQuadTree.Quadrant)i);

                    if (child == null)
                    {
                        continue;
                    }

                    var rect = child.NodeRectangle;

                    var dx = Math.Max(rect.X - p2.X, Math.Max(0, p2.X - rect.Right));
                    var dy = Math.Min(rect.Y - p2.Y, Math.Min(0, p2.Y - rect.Bottom));
                    var actualCost = dx * dx + dy * dy;

                    if (actualCost < bestCost)
                    {
                        bestCost = actualCost;
                        bestChild = child;
                    }
                }

                if (bestChild == null)
                {
                    break;
                }
            }

            node = bestChild;
        }

        return null;
    }

    private struct AStarCell
    {
        public float GCost;

        public Vector2di? Ancestor;
    }

    private sealed class CachedPath
    {
        public Vector2di GoalPos { get; }
        public List<Vector2di> Path { get; }

        public CachedPath(Vector2di goalPos, List<Vector2di> path)
        {
            GoalPos = goalPos;
            Path = path;
        }

        public Vector2di VehiclePos { get; set; }
    }
}