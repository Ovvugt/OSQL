namespace OSQL.Core.Storage;

/// <summary>
/// One table's rows on disk: a heap (unordered collection) of fixed-size pages in a
/// single file, accessed only through the shared <see cref="BufferPool"/>. Inserts go to
/// the last page (or a fresh one); a scan walks every page through the pool, so a table
/// larger than the pool forces real disk reads. Row bytes are produced and interpreted by
/// the <see cref="RowFormat"/>.
/// </summary>
public sealed class HeapFile : IPageDevice, IDisposable
{
    private readonly FileStream _file;
    private readonly BufferPool _pool;
    private readonly RowFormat _format;

    private HeapFile(FileStream file, BufferPool pool, RowFormat format)
    {
        _file = file;
        _pool = pool;
        _format = format;
    }

    /// <summary>Open (or create) the heap file at <paramref name="path"/>.</summary>
    public static HeapFile Open(string path, BufferPool pool, RowFormat format)
    {
        var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return new HeapFile(file, pool, format);
    }

    /// <summary>Insert a row and return its address. The page is left dirty in the pool.</summary>
    public Rid Insert(IReadOnlyList<Value> row)
    {
        var bytes = _format.Encode(row);
        var pageNumber = PageCount == 0 ? AllocatePage() : PageCount - 1;

        var frame = _pool.Fetch(this, pageNumber);
        var page = new HeapPage(frame.Page, _format.RowSize);
        if (page.TryInsert(bytes, out var slot))
        {
            _pool.Unpin(frame, dirty: true);
            return new Rid(pageNumber, slot);
        }

        // The last page is full; start a new one.
        _pool.Unpin(frame, dirty: false);
        pageNumber = AllocatePage();
        frame = _pool.Fetch(this, pageNumber);
        page = new HeapPage(frame.Page, _format.RowSize);
        page.TryInsert(bytes, out slot);
        _pool.Unpin(frame, dirty: true);
        return new Rid(pageNumber, slot);
    }

    /// <summary>Sequentially scan every row in the table, in page-then-slot order.</summary>
    public IEnumerable<(Rid Rid, Value[] Row)> Scan()
    {
        var pageCount = PageCount;
        for (var pageNumber = 0; pageNumber < pageCount; pageNumber++)
        {
            var frame = _pool.Fetch(this, pageNumber);
            try
            {
                var page = new HeapPage(frame.Page, _format.RowSize);
                var rowCount = page.RowCount;
                for (var slot = 0; slot < rowCount; slot++)
                {
                    yield return (new Rid(pageNumber, slot), _format.Decode(page.GetRow(slot)));
                }
            }
            finally
            {
                _pool.Unpin(frame, dirty: false);
            }
        }
    }

    public int PageCount => (int)(_file.Length / Paging.PageSize);

    public void ReadPage(int pageNumber, Span<byte> destination)
    {
        var offset = (long)pageNumber * Paging.PageSize;
        _file.Seek(offset, SeekOrigin.Begin);

        var read = 0;
        while (read < destination.Length)
        {
            var n = _file.Read(destination[read..]);
            if (n == 0)
            {
                destination[read..].Clear(); // past end of file: a fresh page is all zeros
                break;
            }

            read += n;
        }
    }

    public void WritePage(int pageNumber, ReadOnlySpan<byte> source)
    {
        _file.Seek((long)pageNumber * Paging.PageSize, SeekOrigin.Begin);
        _file.Write(source);
    }

    public int AllocatePage()
    {
        var pageNumber = PageCount;
        _file.SetLength((long)(pageNumber + 1) * Paging.PageSize);
        return pageNumber;
    }

    public void Flush() => _file.Flush(flushToDisk: true);

    public void Dispose()
    {
        _pool.Flush(this); // write back dirty pages and fsync before the handle closes
        _file.Dispose();
    }
}
