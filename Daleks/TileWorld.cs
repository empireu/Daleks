using Common;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Daleks;

public sealed class TileWorld : IReadOnlyGrid<TileType>
{

    private static readonly Vector2di[] NeighborOffsets = Enum.GetValues<Direction>().Select(x => x.Step()).ToArray();
   
    private readonly Grid<AStarCell> _pathfindingGrid;
    private readonly Dictionary<TileType, float> _costMap;

    public TileWorld(Vector2di size, Dictionary<TileType, float> costMap)
    {
        _costMap = costMap;
        Size = size;
        Tiles = new Grid<TileType>(size);

        Array.Fill(Tiles.Storage, TileType.Unknown);

        _pathfindingGrid = new Grid<AStarCell>(size);
        ClearPathfindingData();

        _unexploredTree = new BitQuadTree(Vector2di.Zero, Math.Max(size.X, size.Y));

        // Very slow but we're doing it once

        for (var y = 1; y < size.Y - 1; y++)
        {
            for (var x = 1; x < size.X - 1; x++)
            {
                _unexploredTree.Insert(new Vector2di(x, y));
            }
        }
    }
    
    public Vector2di Size { get; private set; }
    public Grid<TileType> Tiles { get; }

    private readonly BitQuadTree _unexploredTree;

    public int UnexploredTreeVersion { get; private set; }

    public IQuadTreeView UnexploredTree => _unexploredTree;

    private void ClearPathfindingData()
    {
        Array.Fill(_pathfindingGrid.Storage, new AStarCell());
    }

    public bool TryFindPath(Vector2di startPoint, Vector2di goalPoint, [NotNullWhen(true)] out List<Vector2di>? path)
    {
        var result = TryFindPathCore(startPoint, goalPoint, out path);
        ClearPathfindingData();

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float NeighborCost(Vector2di neighbor) => _costMap.TryGetValue(Tiles[neighbor], out var c) ? c : 1f;

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

        var queue = new PriorityQueue<Vector2di, float>();

        _pathfindingGrid[startPoint] = new AStarCell(0, Vector2di.DistanceSqr(startPoint, goalPoint));

        queue.Enqueue(startPoint, _pathfindingGrid[startPoint].FCost);

        while (queue.TryDequeue(out var currentPoint, out _))
        {
            if (currentPoint == goalPoint)
            {
                path = new List<Vector2di>();

                while (true)
                {
                    path.Add(goalPoint);

                    goalPoint = _pathfindingGrid[goalPoint].Ancestor;

                    if (goalPoint == startPoint)
                    {
                        path.Add(startPoint);

                        break;
                    }
                }

                path.Reverse();

                return true;
            }

            ref var cell = ref _pathfindingGrid[currentPoint];

            cell.Closed = true;
            cell.InQueue = false;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < NeighborOffsets.Length; i++)
            {
                var offset = NeighborOffsets[i];

                var neighborPos = new Vector2di(currentPoint.X + offset.X, currentPoint.Y + offset.Y);

                if (!Tiles.IsWithinBounds(neighborPos))
                {
                    continue;
                }

                ref var neighborCell = ref _pathfindingGrid[neighborPos];

                if (Tiles[neighborPos].IsObstacle() || neighborCell.Closed)
                {
                    continue;
                }

                var currentGCost = neighborCell.GCost + NeighborCost(neighborPos);

                if (!neighborCell.InQueue)
                {
                    neighborCell.InQueue = true;
                }
                else if (currentGCost >= neighborCell.GCost)
                {
                    continue;
                }

                neighborCell.Ancestor = currentPoint;
                neighborCell.GCost = currentGCost;
                neighborCell.FCost = currentGCost + Vector2di.DistanceSqr(neighborPos, goalPoint);

                queue.Enqueue(neighborPos, neighborCell.FCost);
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
        public float FCost;
        public bool InQueue;
        public bool Closed;

        public Vector2di Ancestor;

        public AStarCell(float gCost, float fCost)
        {
            GCost = gCost;
            FCost = fCost;
        }
    }
}