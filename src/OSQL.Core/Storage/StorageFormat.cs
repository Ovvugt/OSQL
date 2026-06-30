namespace OSQL.Core.Storage;

/// <summary>
/// Identifies the on-disk storage format. This is bumped only when the layout of the
/// data directory or the log records actually changes — never on an ordinary release.
/// The data directory's <see cref="MarkerFileName"/> and every log record's version
/// byte both carry this number, and the server refuses to open data written under a
/// different format. (This is PostgreSQL's <c>PG_VERSION</c> idea: data is keyed to the
/// format version, not the build version, so a rebuild reuses an existing directory.)
/// </summary>
public static class StorageFormat
{
    /// <summary>Current storage-format version. Bump only on an on-disk format change.</summary>
    public const byte Version = 1;

    /// <summary>Marker file naming the format a data directory was written under.</summary>
    public const string MarkerFileName = "FORMAT";

    /// <summary>The append-only command log inside the data directory.</summary>
    public const string LogFileName = "osql.log";
}
