using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

/// <summary>
/// A heap-file stand-in that keeps its pages in memory, so buffer-pool tests can assert
/// on what gets read and written without touching the disk. (NSubstitute doesn't fit here:
/// IPageDevice's Span&lt;byte&gt; parameters can't be used in its argument matchers, and we
/// need real page state across reads and writes anyway.)
/// </summary>
public sealed class InMemoryPageDevice : IPageDevice
{
    private readonly List<byte[]> _pages = new();

    public int Flushes { get; private set; }

    public int PageCount => _pages.Count;

    public void ReadPage(int pageNumber, Span<byte> destination) => _pages[pageNumber].CopyTo(destination);

    public void WritePage(int pageNumber, ReadOnlySpan<byte> source) => source.CopyTo(_pages[pageNumber]);

    public int AllocatePage()
    {
        _pages.Add(new byte[Paging.PageSize]);
        return _pages.Count - 1;
    }

    public void Flush() => Flushes++;

    /// <summary>Read a page's current bytes directly, bypassing the pool (for assertions).</summary>
    public byte[] PageBytes(int pageNumber) => _pages[pageNumber];
}
