namespace OSQL.Core.Storage;

/// <summary>
/// A source of fixed-size pages that the <see cref="BufferPool"/> reads and writes on
/// behalf of higher layers. A heap file is the real implementation; tests provide an
/// in-memory one. The pool is the only thing that calls these — nothing else touches a
/// page directly, which is what makes the pool the single I/O chokepoint.
/// </summary>
public interface IPageDevice
{
    /// <summary>Number of pages the device currently holds.</summary>
    int PageCount { get; }

    /// <summary>Read one page into <paramref name="destination"/> (zero-filled past end of file).</summary>
    void ReadPage(int pageNumber, Span<byte> destination);

    /// <summary>Write one page's bytes back to the device.</summary>
    void WritePage(int pageNumber, ReadOnlySpan<byte> source);

    /// <summary>Grow the device by one page and return its number.</summary>
    int AllocatePage();

    /// <summary>Force buffered writes to durable storage (fsync).</summary>
    void Flush();
}
