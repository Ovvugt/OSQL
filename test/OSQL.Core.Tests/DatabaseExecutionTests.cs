using OSQL.Core;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class DatabaseExecutionTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"osql-exec-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Database OpenWithPeople(string dir)
    {
        var db = Database.Open(dir);
        db.Execute("CREATE TABLE people (id INTEGER, name TEXT)");
        return db;
    }

    [Test]
    public void Insert_ThenSelectStar_ReturnsRows()
    {
        using var db = OpenWithPeople(_dir);
        db.Execute("INSERT INTO people VALUES (1, 'ada')");
        db.Execute("INSERT INTO people VALUES (2, 'alan')");

        var result = db.Execute("SELECT * FROM people");

        Assert.That(result.Rows, Is.Not.Null);
        Assert.That(result.Rows!.Columns, Is.EqualTo(new[] { "id", "name" }));
        Assert.That(result.Rows.Rows, Has.Count.EqualTo(2));
        Assert.That(result.Rows.Rows[0][0], Is.EqualTo(Value.Integer(1)));
        Assert.That(result.Rows.Rows[0][1], Is.EqualTo(Value.Text("ada")));
        Assert.That(result.Rows.Rows[1][1], Is.EqualTo(Value.Text("alan")));
    }

    [Test]
    public void Select_WithWhere_FiltersRows()
    {
        using var db = OpenWithPeople(_dir);
        db.Execute("INSERT INTO people VALUES (1, 'ada')");
        db.Execute("INSERT INTO people VALUES (2, 'alan')");
        db.Execute("INSERT INTO people VALUES (3, 'grace')");

        var result = db.Execute("SELECT * FROM people WHERE id >= 2");

        Assert.That(result.Rows!.Rows.Select(r => r[1].AsText), Is.EqualTo(new[] { "alan", "grace" }));
    }

    [Test]
    public void Select_ProjectsNamedColumns()
    {
        using var db = OpenWithPeople(_dir);
        db.Execute("INSERT INTO people VALUES (1, 'ada')");

        var result = db.Execute("SELECT name FROM people");

        Assert.That(result.Rows!.Columns, Is.EqualTo(new[] { "name" }));
        Assert.That(result.Rows.Rows[0], Has.Count.EqualTo(1));
        Assert.That(result.Rows.Rows[0][0], Is.EqualTo(Value.Text("ada")));
    }

    [Test]
    public void Insert_WithColumnList_DefaultsOmittedColumnsToNull()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (a INTEGER, b TEXT)");
        db.Execute("INSERT INTO t (b) VALUES ('only b')");

        var row = db.Execute("SELECT * FROM t").Rows!.Rows[0];

        Assert.That(row[0].IsNull, Is.True);
        Assert.That(row[1], Is.EqualTo(Value.Text("only b")));
    }

    [Test]
    public void Rows_SurviveAReopen()
    {
        using (var db = OpenWithPeople(_dir))
        {
            db.Execute("INSERT INTO people VALUES (1, 'ada')");
            db.Execute("INSERT INTO people VALUES (2, 'alan')");
        } // Dispose flushes heap pages to disk

        using var reopened = Database.Open(_dir);
        var names = reopened.Execute("SELECT name FROM people").Rows!.Rows.Select(r => r[0].AsText);
        Assert.That(names, Is.EqualTo(new[] { "ada", "alan" }));
    }

    [Test]
    public void Insert_WrongValueCount_Throws()
    {
        using var db = OpenWithPeople(_dir);

        Assert.That(() => db.Execute("INSERT INTO people VALUES (1)"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Insert_TypeMismatch_Throws()
    {
        using var db = OpenWithPeople(_dir);

        // id is INTEGER, but a TEXT literal is supplied.
        Assert.That(() => db.Execute("INSERT INTO people VALUES ('nope', 'ada')"),
            Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Select_NoSuchTable_Throws()
    {
        using var db = Database.Open(_dir);

        Assert.That(() => db.Execute("SELECT * FROM ghosts"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Select_NoSuchColumn_Throws()
    {
        using var db = OpenWithPeople(_dir);

        Assert.That(() => db.Execute("SELECT missing FROM people"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Where_ComparingMismatchedTypes_Throws()
    {
        using var db = OpenWithPeople(_dir);
        db.Execute("INSERT INTO people VALUES (1, 'ada')");

        Assert.That(() => db.Execute("SELECT * FROM people WHERE name >= 5"),
            Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Insert_ExplicitNullIntoNullableColumn_Succeeds()
    {
        using var db = OpenWithPeople(_dir);
        db.Execute("INSERT INTO people VALUES (NULL, 'ada')");

        var row = db.Execute("SELECT * FROM people").Rows!.Rows[0];
        Assert.That(row[0].IsNull, Is.True);
        Assert.That(row[1], Is.EqualTo(Value.Text("ada")));
    }

    [Test]
    public void Insert_ExplicitNullIntoNotNullColumn_Throws()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER NOT NULL, name TEXT)");

        Assert.That(() => db.Execute("INSERT INTO t VALUES (NULL, 'ada')"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Insert_OmittingNotNullColumn_Throws()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER NOT NULL, name TEXT)");

        // The named-column form leaves id NULL by default, which the constraint rejects.
        Assert.That(() => db.Execute("INSERT INTO t (name) VALUES ('ada')"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void NotNullConstraint_SurvivesAReopen()
    {
        using (var db = Database.Open(_dir))
        {
            db.Execute("CREATE TABLE t (id INTEGER NOT NULL)");
        }

        using var reopened = Database.Open(_dir);
        // The constraint was rebuilt from the replayed DDL, so it still applies.
        Assert.That(() => reopened.Execute("INSERT INTO t VALUES (NULL)"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Unique_RejectsADuplicateValue()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER UNIQUE, name TEXT)");
        db.Execute("INSERT INTO t VALUES (1, 'ada')");

        Assert.That(() => db.Execute("INSERT INTO t VALUES (1, 'alan')"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Unique_AllowsDistinctValues()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER UNIQUE, name TEXT)");
        db.Execute("INSERT INTO t VALUES (1, 'ada')");
        db.Execute("INSERT INTO t VALUES (2, 'alan')");

        Assert.That(db.Execute("SELECT * FROM t").Rows!.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void Unique_AllowsMultipleNulls()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER UNIQUE, name TEXT)");

        // NULLs are treated as distinct, so a UNIQUE column may hold many of them.
        db.Execute("INSERT INTO t VALUES (NULL, 'ada')");
        Assert.That(() => db.Execute("INSERT INTO t VALUES (NULL, 'alan')"), Throws.Nothing);
    }

    [Test]
    public void Unique_AppliesToText()
    {
        using var db = Database.Open(_dir);
        db.Execute("CREATE TABLE t (id INTEGER, name TEXT UNIQUE)");
        db.Execute("INSERT INTO t VALUES (1, 'ada')");

        Assert.That(() => db.Execute("INSERT INTO t VALUES (2, 'ada')"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Unique_SurvivesAReopen()
    {
        using (var db = Database.Open(_dir))
        {
            db.Execute("CREATE TABLE t (id INTEGER UNIQUE)");
            db.Execute("INSERT INTO t VALUES (1)");
        }

        using var reopened = Database.Open(_dir);
        Assert.That(() => reopened.Execute("INSERT INTO t VALUES (1)"), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void CreateIndex_IsParsedButNotYetExecuted()
    {
        using var db = OpenWithPeople(_dir);

        var result = db.Execute("CREATE INDEX ON people (id)");

        Assert.That(result.Message, Does.Contain("later milestone"));
    }
}
