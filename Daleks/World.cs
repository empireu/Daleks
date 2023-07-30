using Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Daleks;

public sealed class World : IReadOnlyGrid<TileType>
{
    private static readonly Vector2di[] NeighborOffsets = Enum.GetValues<Direction>().Select(x => x.Step()).ToArray();

    public Vector2di Size { get; private set; }

    public Grid<TileType> Tiles { get; }

    private readonly Grid<AStarCell> _pathfindingGrid;

    public World(Vector2di size)
    {
        Size = size;
        Tiles = new Grid<TileType>(size);

        Array.Fill(Tiles.Storage, TileType.Unknown);

        _pathfindingGrid = new Grid<AStarCell>(size);
        ClearPathfindingData();
    }

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
    private static float NeighborCost(Vector2di current, Vector2di neighbor) => 1; // todo

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

                var currentGCost = neighborCell.GCost + NeighborCost(currentPoint, neighborPos);

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

    public TileType this[int x, int y] => Tiles[x, y];

    public bool IsWithinBounds(int x, int y) => Tiles.IsWithinBounds(x, y);
}