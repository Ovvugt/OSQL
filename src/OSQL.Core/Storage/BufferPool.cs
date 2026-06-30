namespace OSQL.Core.Storage;

/// <summary>
/// A fixed-capacity cache of pages sitting between the heap files and everything above
/// them. A <see cref="Fetch"/> that hits is a RAM read; a miss reads a page from disk —
/// that cache-miss-hits-disk moment is the real I/O bottleneck. When the pool is full it
/// evicts the least-recently-used unpinned page, writing it back first if it is dirty.
/// Pinned pages (in active use) are never evicted.
///
/// Single-threaded use only for now: every caller runs under the database's writer lock.
/// </summary>
public sealed class BufferPool
{
    private readonly int _capacity;
    private readonly Dictionary<(IPageDevice Device, int Page), Frame> _frames = new();
    private readonly LinkedList<Frame> _lru = new(); // most-recently-used at the front

    public BufferPool(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The pool needs at least one frame.");
        }

        _capacity = capacity;
    }

    /// <summary>Pages read from a device because they were not cached (cumulative).</summary>
    public int DiskReads { get; private set; }

    /// <summary>Pages written back to a device on eviction or flush (cumulative).</summary>
    public int DiskWrites { get; private set; }

    /// <summary>Pages currently held in the pool.</summary>
    public int Count => _frames.Count;

    /// <summary>
    /// Get a page, reading it from the device on a miss. The returned frame is pinned and
    /// must be released with <see cref="Unpin"/>.
    /// </summary>
    public Frame Fetch(IPageDevice device, int pageNumber)
    {
        var key = (device, pageNumber);
        if (_frames.TryGetValue(key, out var cached))
        {
            _lru.Remove(cached.Node);
            _lru.AddFirst(cached.Node);
            cached.PinCount++;
            return cached;
        }

        EnsureRoom();

        var buffer = new byte[Paging.PageSize];
        device.ReadPage(pageNumber, buffer);
        DiskReads++;

        var frame = new Frame(device, pageNumber, buffer) { PinCount = 1 };
        frame.Node = _lru.AddFirst(frame);
        _frames[key] = frame;
        return frame;
    }

    /// <summary>Release a frame. Pass <paramref name="dirty"/> when the caller modified the page.</summary>
    public void Unpin(Frame frame, bool dirty)
    {
        if (dirty)
        {
            frame.Dirty = true;
        }

        if (frame.PinCount > 0)
        {
            frame.PinCount--;
        }
    }

    /// <summary>Write back every dirty page belonging to <paramref name="device"/>, fsync it, and drop its frames.</summary>
    public void Flush(IPageDevice device)
    {
        foreach (var frame in _frames.Values.Where(f => f.Device == device).ToList())
        {
            WriteBack(frame);
            _lru.Remove(frame.Node);
            _frames.Remove((frame.Device, frame.PageNumber));
        }

        device.Flush();
    }

    private void EnsureRoom()
    {
        while (_frames.Count >= _capacity)
        {
            var victim = _lru.Last;
            while (victim is not null && victim.Value.PinCount > 0)
            {
                victim = victim.Previous;
            }

            if (victim is null)
            {
                throw new InvalidOperationException("Buffer pool exhausted: every frame is pinned.");
            }

            WriteBack(victim.Value);
            _frames.Remove((victim.Value.Device, victim.Value.PageNumber));
            _lru.Remove(victim);
        }
    }

    private void WriteBack(Frame frame)
    {
        if (!frame.Dirty)
        {
            return;
        }

        frame.Device.WritePage(frame.PageNumber, frame.Page);
        frame.Dirty = false;
        DiskWrites++;
    }

    /// <summary>One cached page: its bytes plus the bookkeeping the pool needs.</summary>
    public sealed class Frame
    {
        internal Frame(IPageDevice device, int pageNumber, byte[] page)
        {
            Device = device;
            PageNumber = pageNumber;
            Page = page;
        }

        /// <summary>The page's bytes. Callers read and write through here.</summary>
        public byte[] Page { get; }

        internal IPageDevice Device { get; }
        internal int PageNumber { get; }
        internal bool Dirty { get; set; }
        internal int PinCount { get; set; }
        internal LinkedListNode<Frame> Node { get; set; } = null!;
    }
}
