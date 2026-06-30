using OSQL.Core.Ast;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class HeapFileTests
{
    private string _path = null!;
    private RowFormat _format = null!;

    [SetUp]
    public void SetUp()
    {
        _path = Path.Combine(Path.GetTempPath(), $"osql-heap-{Guid.NewGuid():N}.heap");
        _format = new RowFormat(new TableSchema("people", new[]
        {
            new ColumnDefinition("id", DataType.Integer),
            new ColumnDefinition("name", DataType.Text),
        }));
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static IReadOnlyList<Value> Person(long id, string name) =>
        new[] { Value.Integer(id), Value.Text(name) };

    [Test]
    public void InsertThenScan_ReturnsRowsWithStableRids()
    {
        var pool = new BufferPool(capacity: 8);
        using var heap = HeapFile.Open(_path, pool, _format);

        var first = heap.Insert(Person(1, "ada"));
        var second = heap.Insert(Person(2, "alan"));

        Assert.That(first, Is.EqualTo(new Rid(0, 0)));
        Assert.That(second, Is.EqualTo(new Rid(0, 1)));

        var rows = heap.Scan().ToList();
        Assert.That(rows.Select(r => r.Rid), Is.EqualTo(new[] { new Rid(0, 0), new Rid(0, 1) }));
        Assert.That(rows[0].Row[1].AsText, Is.EqualTo("ada"));
        Assert.That(rows[1].Row[0].AsInteger, Is.EqualTo(2));
    }

    [Test]
    public void Rows_SurviveReopen()
    {
        var pool = new BufferPool(capacity: 8);
        using (var heap = HeapFile.Open(_path, pool, _format))
        {
            heap.Insert(Person(1, "ada"));
            heap.Insert(Person(2, "alan"));
        } // Dispose flushes dirty pages to disk and fsyncs

        var reopened = HeapFile.Open(_path, new BufferPool(capacity: 8), _format);
        using (reopened)
        {
            Assert.That(reopened.Scan().Select(r => r.Row[1].AsText), Is.EqualTo(new[] { "ada", "alan" }));
        }
    }

    [Test]
    public void Insert_SpillsOntoNewPagesWhenAPageFills()
    {
        var pool = new BufferPool(capacity: 64);
        using var heap = HeapFile.Open(_path, pool, _format);

        var perPage = (Paging.PageSize - HeapPage.HeaderSize) / _format.RowSize;
        var total = perPage + 5; // guarantee a second page

        for (var i = 0; i < total; i++)
        {
            heap.Insert(Person(i, $"row{i}"));
        }

        Assert.That(heap.PageCount, Is.EqualTo(2));

        var scanned = heap.Scan().ToList();
        Assert.That(scanned, Has.Count.EqualTo(total));
        Assert.That(scanned.Last().Rid, Is.EqualTo(new Rid(1, 4)));
    }

    [Test]
    public void Scan_OfTableLargerThanPool_ForcesDiskReads()
    {
        // A one-frame pool cannot hold a multi-page table, so every page in a scan is a miss.
        var pool = new BufferPool(capacity: 1);
        using var heap = HeapFile.Open(_path, pool, _format);

        var perPage = (Paging.PageSize - HeapPage.HeaderSize) / _format.RowSize;
        for (var i = 0; i < perPage * 3; i++)
        {
            heap.Insert(Person(i, "x"));
        }

        Assert.That(heap.PageCount, Is.EqualTo(3));

        var readsBefore = pool.DiskReads;
        var count = heap.Scan().Count();

        Assert.That(count, Is.EqualTo(perPage * 3));
        Assert.That(pool.DiskReads - readsBefore, Is.EqualTo(3)); // one disk read per page
    }
}
