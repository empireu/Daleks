namespace Common;

public interface IReadOnlyGrid<out T>
{
    T this[int x, int y] { get; }
    T this[Vector2di v] => this[v.X, v.Y];
    bool IsWithinBounds(int x, int y);
    bool IsWithinBounds(Vector2di v) => IsWithinBounds(v.X, v.Y);
}

public class Grid<T> : IReadOnlyGrid<T>
{
    public T[] Storage { get; }

    public Vector2di Size { get; }

    public Grid(T[] storage, Vector2di size)
    {
        Storage = storage;
        Size = size;
    }

    public Grid(Vector2di size) : this(new T[size.X * size.Y], size) { }

    private ref T Get(int x, int y) => ref Storage[y * Size.X + x];

    public ref T this[int x, int y] => ref Get(x, y);

    public ref T this[Vector2di v] => ref Get(v.X, v.Y);

    T IReadOnlyGrid<T>.this[int x, int y] => Get(x, y);

    T IReadOnlyGrid<T>.this[Vector2di v] => Get(v.X, v.Y);

    public bool IsWithinBounds(int x, int y) => x >= 0 && x < Size.X && y >= 0 && y < Size.Y;
    
    public bool IsWithinBounds(Vector2di v) => IsWithinBounds(v.X, v.Y);
}

public class HashMultiMap<TKey, TValue> where TKey : notnull
{
    public readonly Dictionary<TKey, HashSet<TValue>> Map = new();

    public HashSet<TValue> this[TKey k] => Map.TryGetValue(k, out var e) ? e : new HashSet<TValue>().Also(s => Map.Add(k, s));

    public void Add(TKey k, TValue v) => this[k].Add(v);

    public bool Contains(TKey k)
    {
        if (!Map.TryGetValue(k, out var set))
        {
            return false;
        }

        return set.Count > 0;
    }

    public bool Remove(TKey k) => Map.Remove(k);

    public bool Remove(TKey k, TValue v) => Map.TryGetValue(k, out var set) && set.Remove(v);
}