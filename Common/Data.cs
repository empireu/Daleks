﻿using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Common;

public interface IReadOnlyGrid<out T>
{
    Vector2ds Size { get; }
    T this[int x, int y] { get; }
    T this[Vector2ds v] => this[v.X, v.Y];
    bool IsWithinBounds(int x, int y);
    bool IsWithinBounds(Vector2ds v);
    public IReadOnlyList<T> Cells { get; }
}

public sealed class Grid<T> : IReadOnlyGrid<T>
{
    public T[] Storage { get; }

    public IReadOnlyList<T> Cells => Storage;

    public Vector2ds Size { get; }

    public Grid(T[] storage, Vector2ds size)
    {
        Storage = storage;
        Size = size;
    }

    public Grid(Vector2ds size) : this(new T[size.X * size.Y], size) { }

    private ref T Get(int x, int y) => ref Storage[y * Size.X + x];

    public ref T this[int x, int y] => ref Get(x, y);

    public ref T this[Vector2ds v] => ref Get(v.X, v.Y);

    T IReadOnlyGrid<T>.this[int x, int y] => Get(x, y);

    T IReadOnlyGrid<T>.this[Vector2ds v] => Get(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(int x, int y) => x >= 0 && x < Size.X && y >= 0 && y < Size.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBounds(Vector2ds v) => IsWithinBounds(v.X, v.Y);

    public Grid<T> Bind() => new(Storage.ToArray(), Size);
}

public interface IReadOnlyMultiMap<TKey, TValue>
{
    int Count { get; }
    IReadOnlyCollection<TKey> Keys { get; }
    IReadOnlySet<TValue> this[TKey k] { get; }
    bool ContainsKey(TKey k);
}

public interface IMultiMap<TKey, TValue> : IReadOnlyMultiMap<TKey, TValue>
{
    bool Add(TKey k, TValue v);
    bool Remove(TKey k);
    bool Remove(TKey k, TValue v);
    void Clear();
}

public sealed class HashMultiMap<TKey, TValue> : IMultiMap<TKey, TValue> where TKey : notnull
{
    public readonly Dictionary<TKey, HashSet<TValue>> Map = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HashSet<TValue> Get(TKey k) =>
        Map.TryGetValue(k, out var set)
            ? set
            : new HashSet<TValue>().Also(s => Map.Add(k, s));

    public int Count => Map.Count;

    public IReadOnlyCollection<TKey> Keys => Map.Keys;

    public HashSet<TValue> this[TKey k] => Get(k);

    IReadOnlySet<TValue> IReadOnlyMultiMap<TKey, TValue>.this[TKey k] => Get(k);

    public bool Add(TKey k, TValue v) => this[k].Add(v);

    public HashSet<TValue>? Place(TKey key, HashSet<TValue> set)
    {
        if (!Map.Remove(key, out var old))
        {
            old = null;
        }

        Map.Add(key, set);

        return old;
    }

    public bool ContainsKey(TKey k)
    {
        if (!Map.TryGetValue(k, out var set))
        {
            return false;
        }

        return set.Count > 0;
    }

    public bool Remove(TKey k) => Map.Remove(k);

    public bool Remove(TKey k, [NotNullWhen(true)] out HashSet<TValue>? set) => Map.Remove(k, out set);

    public bool Remove(TKey k, TValue v) => Map.TryGetValue(k, out var set) && set.Remove(v);

    public void Clear()
    {
        Map.Clear();
    }
}

public interface IReadOnlyBiMap<TForward, TBackward>
{
    IReadOnlyDictionary<TForward, TBackward> Forward { get; }
    IReadOnlyDictionary<TBackward, TForward> Backward { get; }
    bool ContainsForward(TForward f);
    bool ContainsBackward(TBackward b);
}

public interface IBiMap<TForward, TBackward> : IReadOnlyBiMap<TForward, TBackward>
{
    void Associate(TForward f, TBackward b);
    bool Disassociate(TForward f, TBackward b);
    void Clear();
}

public class HashBiMap<TForward, TBackward> : IBiMap<TForward, TBackward> where TForward : notnull where TBackward : notnull
{
    private readonly Dictionary<TForward, TBackward> _forward = new();
    private readonly Dictionary<TBackward, TForward> _backward = new();

    public IReadOnlyDictionary<TForward, TBackward> Forward => _forward;
    public IReadOnlyDictionary<TBackward, TForward> Backward => _backward;

    public bool ContainsForward(TForward f) => Forward.ContainsKey(f);

    public bool ContainsBackward(TBackward b) => Backward.ContainsKey(b);

    public void Associate(TForward f, TBackward b)
    {
        _forward.Add(f, b);
        _backward.Add(b, f);
    }

    public bool Disassociate(TForward f, TBackward b)
    {
        var removedF = _forward.Remove(f, out var actualBackward);
        var removedB = _backward.Remove(b, out var actualForward);

#if DEBUG
        Debug.Assert(removedF == removedB);

        if (removedF)
        {
            Debug.Assert(f.Equals(actualForward));
            Debug.Assert(b.Equals(actualBackward));
        }
#endif

        return removedF;
    }

    public void Clear()
    {
        _forward.Clear();
        _backward.Clear();
    }
}

public sealed class Histogram<TKey> where TKey : notnull
{
    public readonly Dictionary<TKey, int> Map = new();

    public Dictionary<TKey, int>.KeyCollection Keys => Map.Keys;

    public int Count => Map.Count;

    public int this[TKey k]
    {
        get => Map.TryGetValue(k, out var v) ? v : 0;
        set => Map[k] = value;
    }
}

// dotnet/runtime/issues/44871
public class PrioritySet<TElement, TPriority> : IReadOnlyCollection<(TElement Element, TPriority Priority)> where TElement : notnull
{
    private const int DefaultCapacity = 4;

    private readonly IComparer<TPriority> _priorityComparer;
    private readonly Dictionary<TElement, int> _index;

    private HeapEntry[] _heap;
    private int _count;
    private int _version;

    #region Constructors

    public PrioritySet() : this(0, null, null)
    {

    }

    public PrioritySet(int initialCapacity, IComparer<TPriority>? priorityComparer, IEqualityComparer<TElement>? elementComparer)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        if (initialCapacity == 0)
        {
            _heap = Array.Empty<HeapEntry>();
        }
        else
        {
            _heap = new HeapEntry[initialCapacity];
        }

        _index = new Dictionary<TElement, int>(initialCapacity, comparer: elementComparer);
        _priorityComparer = priorityComparer ?? Comparer<TPriority>.Default;
    }

    #endregion

    public int Count => _count;

    public IComparer<TPriority> Comparer => _priorityComparer;

    public void Enqueue(TElement element, TPriority priority)
    {
        if (_index.ContainsKey(element))
        {
            throw new InvalidOperationException("Duplicate element");
        }

        _version++;
        Insert(element, priority);
    }

    public TElement Peek()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("queue is empty");
        }

        return _heap[0].Element;
    }

    public bool TryPeek(out TElement element, out TPriority priority)
    {
        if (_count == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }

        (element, priority) = _heap[0];
        return true;
    }

    public TElement Dequeue()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("queue is empty");
        }

        _version++;
        RemoveIndex(index: 0, out TElement result, out TPriority _);
        return result;
    }

    public TElement Dequeue(out TPriority priority)
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("queue is empty");
        }

        _version++;
        RemoveIndex(index: 0, out TElement result, out priority);
        return result;
    }

    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        if (_count == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }

        _version++;
        RemoveIndex(index: 0, out element, out priority);
        return true;
    }

    public TElement EnqueueDequeue(TElement element, TPriority priority)
    {
        if (_count == 0)
        {
            return element;
        }

        if (_index.ContainsKey(element))
        {
            // Set invariant validation assumes behaviour equivalent to
            // calling Enqueue(); Dequeue() operations sequentially.
            // Might consider changing to a Dequeue(); Enqueue() equivalent
            // which is more forgiving under certain scenaria.
            throw new InvalidOperationException("Duplicate element");
        }

        ref HeapEntry minEntry = ref _heap[0];
        if (_priorityComparer.Compare(priority, minEntry.Priority) <= 0)
        {
            return element;
        }

        _version++;
        TElement minElement = minEntry.Element;
        bool result = _index.Remove(minElement);
        Debug.Assert(result, "could not find element in index");
        SiftDown(index: 0, in element, in priority);
        return minElement;
    }

    public void Clear()
    {
        _version++;
        if (_count > 0)
        {
            //if (RuntimeHelpers.IsReferenceOrContainsReferences<HeapEntry>())
            {
                Array.Clear(_heap, 0, _count);
            }

            _index.Clear();
            _count = 0;
        }
    }

    public void Update(TElement element, TPriority priority)
    {
        _version++;

        if (_index.TryGetValue(element, out var index))
        {
            UpdateIndex(index, priority);
        }
        else
        {
            throw new KeyNotFoundException();
        }
    }

    public void EnqueueOrUpdate(TElement element, TPriority priority)
    {
        _version++;

        if (_index.TryGetValue(element, out var index))
        {
            UpdateIndex(index, priority);
        }
        else
        {
            Insert(element, priority);
        }
    }

    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<(TElement Element, TPriority Priority)> IEnumerable<(TElement Element, TPriority Priority)>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    public struct Enumerator : IEnumerator<(TElement Element, TPriority Priority)>, IEnumerator
    {
        private readonly PrioritySet<TElement, TPriority> _queue;
        private readonly int _version;
        private int _index;
        private (TElement Element, TPriority Priority) _current;

        internal Enumerator(PrioritySet<TElement, TPriority> queue)
        {
            _version = queue._version;
            _queue = queue;
            _index = 0;
            _current = default;
        }

        public bool MoveNext()
        {
            PrioritySet<TElement, TPriority> queue = _queue;

            if (queue._version == _version && _index < queue._count)
            {
                ref HeapEntry entry = ref queue._heap[_index];
                _current = (entry.Element, entry.Priority);
                _index++;
                return true;
            }

            if (queue._version != _version)
            {
                throw new InvalidOperationException("collection was modified");
            }

            return false;
        }

        public (TElement Element, TPriority Priority) Current => _current;
        object IEnumerator.Current => _current;

        public void Reset()
        {
            if (_queue._version != _version)
            {
                throw new InvalidOperationException("collection was modified");
            }

            _index = 0;
            _current = default;
        }

        public void Dispose()
        {
        }
    }

    #region Private Methods

    private void Heapify()
    {
        HeapEntry[] heap = _heap;

        for (int i = (_count - 1) >> 2; i >= 0; i--)
        {
            HeapEntry entry = heap[i]; // ensure struct is copied before sifting
            SiftDown(i, in entry.Element, in entry.Priority);
        }
    }

    private void Insert(in TElement element, in TPriority priority)
    {
        if (_count == _heap.Length)
        {
            Resize(ref _heap);
        }

        SiftUp(index: _count++, in element, in priority);
    }

    private void RemoveIndex(int index, out TElement element, out TPriority priority)
    {
        Debug.Assert(index < _count);

        (element, priority) = _heap[index];

        int lastElementPos = --_count;
        ref HeapEntry lastElement = ref _heap[lastElementPos];

        if (lastElementPos > 0)
        {
            SiftDown(index, in lastElement.Element, in lastElement.Priority);
        }

        //if (RuntimeHelpers.IsReferenceOrContainsReferences<HeapEntry>())
        {
            lastElement = default;
        }

        bool result = _index.Remove(element);
        Debug.Assert(result, "could not find element in index");
    }

    private void UpdateIndex(int index, TPriority newPriority)
    {
        TElement element;
        ref HeapEntry entry = ref _heap[index];

        switch (_priorityComparer.Compare(newPriority, entry.Priority))
        {
            // priority is decreased, sift upward
            case < 0:
                element = entry.Element; // make a copy of the element before sifting
                SiftUp(index, element, newPriority);
                return;

            // priority is increased, sift downward
            case > 0:
                element = entry.Element; // make a copy of the element before sifting
                SiftDown(index, element, newPriority);
                return;

            // priority is same as before, take no action
            default: return;
        }
    }

    private void AppendRaw(IEnumerable<(TElement Element, TPriority Priority)> values)
    {
        // TODO: specialize on ICollection types
        var heap = _heap;
        var index = _index;
        int count = _count;

        foreach ((TElement element, TPriority priority) in values)
        {
            if (count == heap.Length)
            {
                Resize(ref heap);
            }

            if (!index.TryAdd(element, count))
            {
                throw new ArgumentException("duplicate elements", nameof(values));
            }

            ref HeapEntry entry = ref heap[count];
            entry.Element = element;
            entry.Priority = priority;
            count++;
        }

        _heap = heap;
        _count = count;
    }

    private void SiftUp(int index, in TElement element, in TPriority priority)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) >> 2;
            ref HeapEntry parent = ref _heap[parentIndex];

            if (_priorityComparer.Compare(parent.Priority, priority) <= 0)
            {
                // parentPriority <= priority, heap property is satisfed
                break;
            }

            _heap[index] = parent;
            _index[parent.Element] = index;
            index = parentIndex;
        }

        ref HeapEntry entry = ref _heap[index];
        entry.Element = element;
        entry.Priority = priority;
        _index[element] = index;
    }

    private void SiftDown(int index, in TElement element, in TPriority priority)
    {
        int minChildIndex;
        int count = _count;
        HeapEntry[] heap = _heap;

        while ((minChildIndex = (index << 2) + 1) < count)
        {
            // find the child with the minimal priority
            ref HeapEntry minChild = ref heap[minChildIndex];
            int childUpperBound = Math.Min(count, minChildIndex + 4);

            for (int nextChildIndex = minChildIndex + 1; nextChildIndex < childUpperBound; nextChildIndex++)
            {
                ref HeapEntry nextChild = ref heap[nextChildIndex];
                if (_priorityComparer.Compare(nextChild.Priority, minChild.Priority) < 0)
                {
                    minChildIndex = nextChildIndex;
                    minChild = ref nextChild;
                }
            }

            // compare with inserted priority
            if (_priorityComparer.Compare(priority, minChild.Priority) <= 0)
            {
                // priority <= childPriority, heap property is satisfied
                break;
            }

            heap[index] = minChild;
            _index[minChild.Element] = index;
            index = minChildIndex;
        }

        ref HeapEntry entry = ref heap[index];
        entry.Element = element;
        entry.Priority = priority;
        _index[element] = index;
    }

    private void Resize(ref HeapEntry[] heap)
    {
        int newSize = heap.Length == 0 ? DefaultCapacity : 2 * heap.Length;
        Array.Resize(ref heap, newSize);
    }

    private struct HeapEntry
    {
        public TElement Element;
        public TPriority Priority;

        public void Deconstruct(out TElement element, out TPriority priority)
        {
            element = Element;
            priority = Priority;
        }
    }

#if DEBUG
    public void ValidateInternalState()
    {
        if (_heap.Length < _count)
        {
            throw new Exception("invalid elements array length");
        }

        if (_index.Count != _count)
        {
            throw new Exception("Invalid heap index count");
        }

        foreach ((var element, var idx) in _heap.Select((x, i) => (x.Element, i)).Skip(_count))
        {
            if (!IsDefault(element))
            {
                throw new Exception($"Non-zero element '{element}' at index {idx}.");
            }
        }

        foreach ((var priority, var idx) in _heap.Select((x, i) => (x.Priority, i)).Skip(_count))
        {
            if (!IsDefault(priority))
            {
                throw new Exception($"Non-zero priority '{priority}' at index {idx}.");
            }
        }

        foreach (var kvp in _index)
        {
            if (!_index.Comparer.Equals(_heap[kvp.Value].Element, kvp.Key))
            {
                throw new Exception($"Element '{kvp.Key}' maps to invalid heap location {kvp.Value} which contains '{_heap[kvp.Value].Element}'");
            }
        }

        static bool IsDefault<T>(T value)
        {
            T defaultVal = default;

            if (defaultVal is null)
            {
                return value is null;
            }

            return value!.Equals(defaultVal);
        }
    }
#endif
    #endregion
}