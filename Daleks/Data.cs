namespace Daleks;

public class MultiMap<TKey, TValue> where TKey : notnull
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

    public void Remove(TKey k) => Map.Remove(k);
}