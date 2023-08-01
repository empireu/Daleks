using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using static Common.BitQuadTree;

namespace Common;

public interface IReadOnlyGrid<out T>
{
    Vector2di Size { get; }
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

// Super duper memory inefficient but enough for this usage

public interface IQuadTreeView
{
    Vector2di Position { get; }
    ushort Size { get; }
    Rectangle NodeRectangle { get; }
    bool IsFilled { get; }
    bool HasChildren { get; }
    bool HasTiles { get; }
    IQuadTreeView? GetChildView(Quadrant quadrant);
    IQuadTreeView? GetChildView(Vector2di position);
    Quadrant GetQuadrant(Vector2di position);
}

public sealed class BitQuadTree : IQuadTreeView
{
    public enum Quadrant : byte
    {
        BottomLeft = 0,
        BottomRight = 1,
        TopLeft = 2,
        TopRight = 3
    }

    public Vector2di Position { get; }

    private readonly byte _log;

    private BitQuadTree? _bl;
    private BitQuadTree? _br;
    private BitQuadTree? _tl;
    private BitQuadTree? _tr;

    public BitQuadTree(Vector2di position, int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), $"Quadtree size cannot be {size}");
        }

        Position = position;

        _log = (byte)Math.Log(NextPow2(size), 2);
    }

    private BitQuadTree(Vector2di position, byte log)
    {
        Position = position;
        _log = log;
    }

    public bool IsFilled { get; private set; }

    public ushort Size => (ushort)(1 << _log);

    public Rectangle NodeRectangle => new(Position.X, Position.Y, Size, Size);

    public bool HasChildren
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsFilled && (_bl != null || _br != null || _tl != null || _tr != null);
    }

    public bool HasTiles
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFilled || (_bl != null || _br != null || _tl != null || _tr != null);
    }

    public IQuadTreeView? GetChildView(Quadrant quadrant) => GetChild(quadrant);
    public IQuadTreeView? GetChildView(Vector2di position) => GetChild(position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitQuadTree? GetChild(Quadrant quadrant) => quadrant switch
    {
        Quadrant.BottomLeft => _bl,
        Quadrant.BottomRight => _br,
        Quadrant.TopLeft => _tl,
        Quadrant.TopRight => _tr,
        _ => throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant, null)
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private BitQuadTree? SetChild(Quadrant quadrant, BitQuadTree? child) => quadrant switch
    {
        Quadrant.BottomLeft => _bl = child,
        Quadrant.BottomRight => _br = child,
        Quadrant.TopLeft => _tl = child,
        Quadrant.TopRight => _tr = child,
        _ => throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant, null)
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitQuadTree? GetChild(Vector2di position) => GetChild(GetQuadrant(position));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Quadrant GetQuadrant(Vector2di position)
    {
        var isLeft = position.X < Position.X + Size / 2;
        var isBottom = position.Y < Position.Y + Size / 2;

        if (isBottom)
        {
            return isLeft ? Quadrant.BottomLeft : Quadrant.BottomRight;
        }

        return isLeft ? Quadrant.TopLeft : Quadrant.TopRight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private BitQuadTree CreateNode(Quadrant quadrant)
    {
        var x = Position.X;
        var y = Position.Y;

        var childSize = (ushort)(Size / 2);
        var childSizeLog = (byte)(_log - 1);

        return quadrant switch
        {
            Quadrant.BottomLeft => _bl = new BitQuadTree(new Vector2di(x, y), childSizeLog),
            Quadrant.BottomRight => _br = new BitQuadTree(new Vector2di(x + childSize, y), childSizeLog),
            Quadrant.TopLeft => _tl = new BitQuadTree(new Vector2di(x, y + childSize), childSizeLog),
            Quadrant.TopRight => _tr = new BitQuadTree(new Vector2di(x + childSize, y + childSize), childSizeLog),
            _ => throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant, null)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void KillChildren()
    {
        _bl = null;
        _br = null;
        _tl = null;
        _tr = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private BitQuadTree GetOrCreateChild(Vector2di position)
    {
        var subNodeIndex = GetQuadrant(position);
        var subNode = GetChild(subNodeIndex) ?? CreateNode(subNodeIndex);
        return subNode;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(Vector2di tile)
    {
        if (!NodeRectangle.Contains(tile))
        {
            throw new InvalidOperationException("Cannot insert outside of bounds");
        }

        InsertCore(tile, 0);
    }

    private void InsertCore(Vector2di position, byte sizeExp)
    {
        if (IsFilled)
        {
            return;
        }

        if (_log == sizeExp)
        {
            if (IsFilled)
            {
                return;
            }

            KillChildren();
            IsFilled = true;
            return;
        }

        if (_log == 0)
        {
            throw new InvalidOperationException("Tried to insert in leaf node");
        }

        var child = GetOrCreateChild(position);
        
        child.InsertCore(position, sizeExp);
        
        Optimize();
    }

    public bool Remove(Vector2di tile)
    {
        if (!NodeRectangle.Contains(tile))
        {
            return false;
        }

        return RemoveCore(tile);
    }

    private bool RemoveCore(Vector2di tile)
    {
        if (IsFilled)
        {
            IsFilled = false;

            for (byte i = 0; i < 4; i++)
            {
                CreateNode((Quadrant)i).IsFilled = true;
            }
        }

        var quadrant = GetQuadrant(tile);
        var child = GetChild(quadrant);

        if (child == null)
        {
            return false;
        }

        if (child._log == 0)
        {
            SetChild(quadrant, null);
            return true;
        }

        var removed = child.RemoveCore(tile);

        if (removed && !child.HasTiles)
        {
            SetChild(quadrant, null);
        }

        return removed;
    }

    private void Optimize()
    {
        for (byte subNodeIndex = 0; subNodeIndex < 4; subNodeIndex++)
        {
            var node = GetChild((Quadrant)subNodeIndex);
         
            if (node is not { IsFilled: true })
            {
                return;
            }
        }

        KillChildren();

        IsFilled = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Vector2di position)
    {
        if (!NodeRectangle.Contains(position))
        {
            return false;
        }

        if (!HasChildren)
        {
            return IsFilled;
        }

        var node = GetChild(position);

        return node != null && node.Contains(position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPow2(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }
}

public interface IReadOnlyHashMultiMap<in TKey, TValue>
{
    IReadOnlySet<TValue> this[TKey k] { get; }
    bool ContainsKey(TKey k);
}

public class HashMultiMap<TKey, TValue> : IReadOnlyHashMultiMap<TKey, TValue> where TKey : notnull
{
    public readonly Dictionary<TKey, HashSet<TValue>> Map = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HashSet<TValue> Get(TKey k) => Map.TryGetValue(k, out var e) ? e : new HashSet<TValue>().Also(s => Map.Add(k, s));

    public HashSet<TValue> this[TKey k] => Get(k);

    IReadOnlySet<TValue> IReadOnlyHashMultiMap<TKey, TValue>.this[TKey k] => Get(k);

    public void Add(TKey k, TValue v) => this[k].Add(v);

    public bool ContainsKey(TKey k)
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