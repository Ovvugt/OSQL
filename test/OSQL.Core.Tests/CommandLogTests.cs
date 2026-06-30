using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class CommandLogTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _path = Path.Combine(Path.GetTempPath(), $"osql-log-{Guid.NewGuid():N}.log");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private IReadOnlyList<string> ReadAll()
    {
        using var log = CommandLog.Open(_path);
        return log.ReadAll();
    }

    private void Append(params string[] statements)
    {
        using var log = CommandLog.Open(_path);
        foreach (var statement in statements)
        {
            log.Append(statement);
        }
    }

    [Test]
    public void ReadAll_OnEmptyLog_ReturnsNothing()
    {
        Assert.That(ReadAll(), Is.Empty);
    }

    [Test]
    public void AppendThenReadAll_RoundTripsRecordsInOrder()
    {
        Append("CREATE TABLE a (x INTEGER)", "CREATE TABLE b (y TEXT)");

        Assert.That(ReadAll(), Is.EqualTo(new[]
        {
            "CREATE TABLE a (x INTEGER)",
            "CREATE TABLE b (y TEXT)",
        }));
    }

    [Test]
    public void ReadAll_PreservesNewlinesInsidePayloads()
    {
        // The whole reason we chose length-prefixed framing over newline-delimited.
        Append("INSERT INTO t VALUES ('line one\nline two')");

        Assert.That(ReadAll()[0], Is.EqualTo("INSERT INTO t VALUES ('line one\nline two')"));
    }

    [Test]
    public void ReadAll_WithTruncatedFinalRecord_DropsItAndTrimsTheFile()
    {
        Append("CREATE TABLE a (x INTEGER)", "CREATE TABLE b (y TEXT)");

        // Simulate a crash mid-append: lop bytes off the tail.
        using (var raw = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite))
        {
            raw.SetLength(raw.Length - 4);
        }

        // The torn record is gone, and the file was trimmed so future appends are clean.
        Assert.That(ReadAll(), Is.EqualTo(new[] { "CREATE TABLE a (x INTEGER)" }));

        Append("CREATE TABLE c (z INTEGER)");
        Assert.That(ReadAll(), Is.EqualTo(new[]
        {
            "CREATE TABLE a (x INTEGER)",
            "CREATE TABLE c (z INTEGER)",
        }));
    }

    [Test]
    public void ReadAll_WithCorruptFinalRecord_RecoversAsTornTail()
    {
        Append("CREATE TABLE a (x INTEGER)", "CREATE TABLE b (y TEXT)");

        // Flip the very last byte: the final record is fully present but fails its CRC,
        // and nothing follows it — so it's treated as a torn tail and dropped.
        FlipByte(_path, offset: new FileInfo(_path).Length - 1);

        Assert.That(ReadAll(), Is.EqualTo(new[] { "CREATE TABLE a (x INTEGER)" }));
    }

    [Test]
    public void ReadAll_WithCorruptInteriorRecord_ThrowsLogCorruption()
    {
        Append("CREATE TABLE a (x INTEGER)", "CREATE TABLE b (y TEXT)");

        // Corrupt the first record's payload (offset 9 = just past its header). Because a
        // valid record follows, this is real corruption, not a recoverable tail.
        FlipByte(_path, offset: 9);

        Assert.That(() => ReadAll(), Throws.TypeOf<LogCorruptionException>());
    }

    private static void FlipByte(string path, long offset)
    {
        using var raw = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        raw.Seek(offset, SeekOrigin.Begin);
        var b = raw.ReadByte();
        raw.Seek(offset, SeekOrigin.Begin);
        raw.WriteByte((byte)(b ^ 0xFF));
    }
}
