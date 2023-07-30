namespace Common;

public class Grid<T>
{
    public T[] Storage { get; }

    public Vector2di Size { get; }

    public Grid(T[] storage, Vector2di size)
    {
        Storage = storage;
        Size = size;
    }

    public Grid(Vector2di size) : this(new T[size.X * size.Y], size) { }

    public ref T this[int x, int y] => ref Storage[y * Size.X + x];

    public ref T this[Vector2di v] => ref this[v.X, v.Y];

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