using OSQL.Core.Ast;

namespace OSQL.Core.Storage;

/// <summary>
/// The running database: the in-memory <see cref="Catalog"/>, the append-only
/// <see cref="CommandLog"/> beneath it, and the single global writer lock that
/// serializes all mutations into one honest, serializable order.
///
/// Two paths share the same mutations:
/// <list type="bullet">
/// <item><b>Live</b> — <see cref="Execute"/>: parse, validate, append + fsync, then apply
/// to memory. Validation happens before the append so an invalid statement is never
/// written, and the write is made durable before the change is acknowledged.</item>
/// <item><b>Replay</b> — on <see cref="Open"/>: fold each logged statement back into the
/// catalog. No locking (single-threaded) and no re-logging.</item>
/// </list>
/// </summary>
public sealed class Database : IDisposable
{
    private readonly Catalog _catalog = new();
    private readonly CommandLog _log;
    private readonly object _writeLock = new();

    private Database(CommandLog log) => _log = log;

    /// <summary>
    /// Open the database rooted at <paramref name="dataDirectory"/>, creating it on first
    /// run, verifying its storage format, and replaying the log to rebuild state.
    /// </summary>
    public static Database Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        EnsureCompatibleFormat(dataDirectory);

        var log = CommandLog.Open(Path.Combine(dataDirectory, StorageFormat.LogFileName));
        var database = new Database(log);
        database.Replay();
        return database;
    }

    /// <summary>Snapshot of the tables that currently exist.</summary>
    public IReadOnlyCollection<TableSchema> Tables
    {
        get
        {
            lock (_writeLock)
            {
                return _catalog.Tables.ToList();
            }
        }
    }

    /// <summary>
    /// Run one statement as an implicit transaction and return a human-readable result.
    /// Throws <see cref="OsqlException"/> for anything the client did wrong.
    /// </summary>
    public string Execute(string sql)
    {
        var statement = Sql.Parse(sql);

        lock (_writeLock)
        {
            return statement switch
            {
                CreateTableStatement create => ExecuteCreateTable(create, sql),
                _ => $"Parsed {DescribeKind(statement)}; execution arrives in a later milestone "
                     + "(nothing was stored).",
            };
        }
    }

    private string ExecuteCreateTable(CreateTableStatement statement, string sql)
    {
        var schema = BuildSchema(statement); // rejects duplicate columns

        // Validate before we write: an invalid statement must never reach the log.
        if (_catalog.Contains(schema.Name))
        {
            throw new OsqlException($"Table '{schema.Name}' already exists.");
        }

        _log.Append(sql);     // durable before visible
        _catalog.Add(schema); // already validated, so this cannot fail

        return $"Table '{schema.Name}' created with {schema.Columns.Count} column(s).";
    }

    private void Replay()
    {
        foreach (var sql in _log.ReadAll())
        {
            try
            {
                if (Sql.Parse(sql) is CreateTableStatement create)
                {
                    _catalog.Add(BuildSchema(create));
                }
                else
                {
                    // Only CREATE TABLE is durable in this format, so nothing else
                    // should ever be in the log.
                    throw new OsqlException($"unexpected {DescribeKind(Sql.Parse(sql))} in the log");
                }
            }
            catch (OsqlException ex)
            {
                // The bytes passed their checksum but no longer form a statement we can
                // apply — treat that as corruption rather than guessing.
                throw new LogCorruptionException($"Could not replay logged statement \"{sql}\": {ex.Message}");
            }
        }
    }

    private static TableSchema BuildSchema(CreateTableStatement statement)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in statement.Columns)
        {
            if (!seen.Add(column.Name))
            {
                throw new OsqlException(
                    $"Duplicate column '{column.Name}' in table '{statement.TableName}'.");
            }
        }

        return new TableSchema(statement.TableName, statement.Columns);
    }

    private static void EnsureCompatibleFormat(string dataDirectory)
    {
        var markerPath = Path.Combine(dataDirectory, StorageFormat.MarkerFileName);

        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, StorageFormat.Version.ToString());
            return;
        }

        var found = File.ReadAllText(markerPath).Trim();
        if (!byte.TryParse(found, out var version) || version != StorageFormat.Version)
        {
            throw new StorageFormatException(
                $"Data directory '{dataDirectory}' was written under storage format '{found}', "
                + $"but this build speaks format {StorageFormat.Version}.");
        }
    }

    private static string DescribeKind(SqlStatement statement) => statement switch
    {
        CreateTableStatement => "CREATE TABLE",
        InsertStatement => "INSERT",
        SelectStatement => "SELECT",
        CreateIndexStatement => "CREATE INDEX",
        _ => statement.GetType().Name,
    };

    public void Dispose() => _log.Dispose();
}
