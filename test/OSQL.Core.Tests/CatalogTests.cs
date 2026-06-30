using OSQL.Core;
using OSQL.Core.Ast;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class CatalogTests
{
    private static TableSchema Schema(string name) =>
        new(name, new[] { new ColumnDefinition("id", DataType.Integer) });

    [Test]
    public void Add_ThenContainsAndGet_FindTheTable()
    {
        var catalog = new Catalog();
        catalog.Add(Schema("users"));

        Assert.That(catalog.Contains("users"), Is.True);
        Assert.That(catalog.Get("users").Name, Is.EqualTo("users"));
    }

    [Test]
    public void Contains_IsCaseInsensitive()
    {
        var catalog = new Catalog();
        catalog.Add(Schema("Users"));

        Assert.That(catalog.Contains("users"), Is.True);
    }

    [Test]
    public void Add_DuplicateName_Throws()
    {
        var catalog = new Catalog();
        catalog.Add(Schema("users"));

        Assert.That(() => catalog.Add(Schema("USERS")), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Get_MissingTable_Throws()
    {
        Assert.That(() => new Catalog().Get("nope"), Throws.TypeOf<OsqlException>());
    }
}
