using OSQL.Core;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class DatabaseTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"osql-db-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Test]
    public void CreateTable_IsVisibleWithinTheSession()
    {
        using var db = Database.Open(_dir);

        db.Execute("CREATE TABLE users (id INTEGER, name TEXT)");

        Assert.That(db.Tables.Select(t => t.Name), Is.EqualTo(new[] { "users" }));
    }

    [Test]
    public void CreateTable_SurvivesAReopen()
    {
        using (var db = Database.Open(_dir))
        {
            db.Execute("CREATE TABLE users (id INTEGER, name TEXT)");
        }

        using var reopened = Database.Open(_dir);
        Assert.That(reopened.Tables.Select(t => t.Name), Is.EqualTo(new[] { "users" }));
        Assert.That(reopened.Tables.Single().Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateTable_DuplicateName_IsRejectedAndNotPersisted()
    {
        using (var db = Database.Open(_dir))
        {
            db.Execute("CREATE TABLE users (id INTEGER)");
            Assert.That(() => db.Execute("CREATE TABLE users (id INTEGER)"),
                Throws.TypeOf<OsqlException>());
        }

        // The rejected statement must never have reached the log.
        using var reopened = Database.Open(_dir);
        Assert.That(reopened.Tables, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateTable_DuplicateColumn_IsRejected()
    {
        using var db = Database.Open(_dir);

        Assert.That(() => db.Execute("CREATE TABLE t (id INTEGER, id TEXT)"),
            Throws.TypeOf<OsqlException>());
        Assert.That(db.Tables, Is.Empty);
    }

    [Test]
    public void NonCreateStatement_ParsesButIsNotStored()
    {
        using (var db = Database.Open(_dir))
        {
            var reply = db.Execute("INSERT INTO t VALUES (1)");
            Assert.That(reply, Does.Contain("later milestone"));
        }

        using var reopened = Database.Open(_dir);
        Assert.That(reopened.Tables, Is.Empty);
    }

    [Test]
    public void Open_WithIncompatibleFormatMarker_Throws()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, StorageFormat.MarkerFileName), "99");

        Assert.That(() => Database.Open(_dir), Throws.TypeOf<StorageFormatException>());
    }

    [Test]
    public void Open_OnFreshDirectory_WritesTheFormatMarker()
    {
        using (Database.Open(_dir)) { }

        var marker = Path.Combine(_dir, StorageFormat.MarkerFileName);
        Assert.That(File.Exists(marker), Is.True);
        Assert.That(File.ReadAllText(marker).Trim(), Is.EqualTo(StorageFormat.Version.ToString()));
    }
}
