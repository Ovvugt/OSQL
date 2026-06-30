using OSQL.Core;
using OSQL.Core.Ast;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class CatalogTests
{
    private string _dir = null!;
    private BufferPool _pool = null!;
    private readonly List<Table> _open = new();

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"osql-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _pool = new BufferPool(capacity: 8);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var table in _open)
        {
            table.Dispose();
        }

        _open.Clear();

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private Table MakeTable(int id, string name)
    {
        var schema = new TableSchema(name, new[] { new ColumnDefinition("id", DataType.Integer) });
        var heap = HeapFile.Open(Path.Combine(_dir, $"{id}.heap"), _pool, new RowFormat(schema));
        var table = new Table(id, schema, heap);
        _open.Add(table);
        return table;
    }

    [Test]
    public void Add_ThenContainsAndGet_FindTheTable()
    {
        var catalog = new Catalog();
        catalog.Add(MakeTable(1, "users"));

        Assert.That(catalog.Contains("users"), Is.True);
        Assert.That(catalog.Get("users").Name, Is.EqualTo("users"));
    }

    [Test]
    public void Contains_IsCaseInsensitive()
    {
        var catalog = new Catalog();
        catalog.Add(MakeTable(1, "Users"));

        Assert.That(catalog.Contains("users"), Is.True);
    }

    [Test]
    public void Add_DuplicateName_Throws()
    {
        var catalog = new Catalog();
        catalog.Add(MakeTable(1, "users"));

        Assert.That(() => catalog.Add(MakeTable(2, "USERS")), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Get_MissingTable_Throws()
    {
        Assert.That(() => new Catalog().Get("nope"), Throws.TypeOf<OsqlException>());
    }
}
