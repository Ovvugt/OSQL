using OSQL.Core.Ast;

namespace OSQL.Core.Storage;

/// <summary>
/// The running database. It owns the shared <see cref="BufferPool"/>, the per-table heap
/// files, the DDL <see cref="CommandLog"/>, and the single global writer lock that
/// serializes every statement.
///
/// DDL (CREATE TABLE) is durable through the log and replayed on open. Rows go to on-disk
/// heap files, not the log, and are read back through the buffer pool — so a table larger
/// than the pool incurs real disk I/O. INSERTs are buffered and flushed on a clean shutdown
/// (see <see cref="Dispose"/>); crash-safety for rows arrives with the write-ahead log in
/// the transactions milestone.
/// </summary>
public sealed class Database : IDisposable
{
    private const int BufferPoolFrames = 128;

    private readonly string _dataDirectory;
    private readonly BufferPool _pool;
    private readonly CommandLog _log;
    private readonly Catalog _catalog = new();
    private readonly Dictionary<int, SequenceFile> _sequences = new(); // table id -> its SERIAL counters
    private readonly object _writeLock = new();
    private int _nextTableId = 1;

    private Database(string dataDirectory, BufferPool pool, CommandLog log)
    {
        _dataDirectory = dataDirectory;
        _pool = pool;
        _log = log;
    }

    /// <summary>Open the database, creating and format-checking the directory and replaying the DDL log.</summary>
    public static Database Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        EnsureCompatibleFormat(dataDirectory);

        var pool = new BufferPool(BufferPoolFrames);
        var log = CommandLog.Open(Path.Combine(dataDirectory, StorageFormat.LogFileName));
        var database = new Database(dataDirectory, pool, log);
        database.Replay();
        return database;
    }

    /// <summary>Snapshot of the schemas of the tables that currently exist.</summary>
    public IReadOnlyCollection<TableSchema> Tables
    {
        get
        {
            lock (_writeLock)
            {
                return _catalog.Tables.Select(t => t.Schema).ToList();
            }
        }
    }

    /// <summary>Run one statement as an implicit transaction. Throws <see cref="OsqlException"/> on user errors.</summary>
    public ExecutionResult Execute(string sql)
    {
        var statement = Sql.Parse(sql);

        lock (_writeLock)
        {
            return statement switch
            {
                CreateTableStatement create => ExecuteCreateTable(create, sql),
                InsertStatement insert => ExecuteInsert(insert),
                SelectStatement select => ExecuteSelect(select),
                _ => ExecutionResult.Status(
                    $"Parsed {DescribeKind(statement)}; execution arrives in a later milestone."),
            };
        }
    }

    // ---- CREATE TABLE ----

    private ExecutionResult ExecuteCreateTable(CreateTableStatement statement, string sql)
    {
        var schema = BuildSchema(statement); // rejects duplicate columns

        if (_catalog.Contains(schema.Name))
        {
            throw new OsqlException($"Table '{schema.Name}' already exists.");
        }

        _log.Append(sql);      // DDL is durable in the log; validated above so it is safe to log
        RegisterTable(schema); // create the heap file and register the table

        return ExecutionResult.Status($"Table '{schema.Name}' created with {schema.Columns.Count} column(s).");
    }

    private Table RegisterTable(TableSchema schema)
    {
        var id = _nextTableId++;
        var heapPath = Path.Combine(_dataDirectory, $"{id}.heap");
        var heap = HeapFile.Open(heapPath, _pool, new RowFormat(schema));
        var table = new Table(id, schema, heap);
        _catalog.Add(table);

        var serialColumns = Enumerable.Range(0, schema.Columns.Count)
            .Where(i => schema.Columns[i].Generated == ColumnGeneration.Serial)
            .ToList();
        if (serialColumns.Count > 0)
        {
            _sequences[id] = SequenceFile.Open(Path.Combine(_dataDirectory, $"{id}.seq"), serialColumns);
        }

        return table;
    }

    // ---- INSERT ----

    private ExecutionResult ExecuteInsert(InsertStatement statement)
    {
        var table = _catalog.Get(statement.TableName);
        var row = BuildRow(table.Schema, statement);
        ApplySerials(table, row);          // fill omitted/NULL serial columns with generated values
        EnforceNotNull(table.Schema, row); // after generation, so a generated value satisfies it
        EnforceUnique(table, row);         // table scan for now; an index probe later
        table.Heap.Insert(row);            // buffered; reaches disk on flush or eviction
        return ExecutionResult.Status($"1 row inserted into '{table.Name}'.");
    }

    private static Value[] BuildRow(TableSchema schema, InsertStatement statement)
    {
        var columns = schema.Columns;
        var values = new Value[columns.Count];

        if (statement.Columns is null)
        {
            // Positional: every column, in order.
            if (statement.Values.Count != columns.Count)
            {
                throw new OsqlException(
                    $"Table '{schema.Name}' has {columns.Count} column(s) but {statement.Values.Count} value(s) were given.");
            }

            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = LiteralToValue(statement.Values[i]);
            }

            return values;
        }

        // Named columns: the rest default to NULL.
        if (statement.Columns.Count != statement.Values.Count)
        {
            throw new OsqlException(
                $"{statement.Columns.Count} column(s) named but {statement.Values.Count} value(s) given.");
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Value.Null;
        }

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var k = 0; k < statement.Columns.Count; k++)
        {
            var name = statement.Columns[k];
            if (!assigned.Add(name))
            {
                throw new OsqlException($"Column '{name}' specified more than once.");
            }

            values[IndexOfColumn(schema, name)] = LiteralToValue(statement.Values[k]);
        }

        return values;
    }

    // Fill each SERIAL column: a NULL (omitted or written explicitly) gets the next generated
    // value; an explicitly supplied value is used as-is and advances the counter past it.
    private void ApplySerials(Table table, Value[] row)
    {
        if (!_sequences.TryGetValue(table.Id, out var sequences))
        {
            return;
        }

        var columns = table.Schema.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Generated != ColumnGeneration.Serial)
            {
                continue;
            }

            if (row[i].IsNull)
            {
                row[i] = Value.Integer(sequences.Next(i));
            }
            else
            {
                sequences.Observe(i, row[i].AsInteger);
            }
        }
    }

    private static void EnforceNotNull(TableSchema schema, Value[] values)
    {
        for (var i = 0; i < schema.Columns.Count; i++)
        {
            if (schema.Columns[i].NotNull && values[i].IsNull)
            {
                throw new OsqlException(
                    $"Column '{schema.Columns[i].Name}' is declared NOT NULL but the value is NULL.");
            }
        }
    }

    // Reject a row that would duplicate a non-NULL value in a UNIQUE column. NULLs never
    // conflict (SQL treats them as distinct), so they are skipped. This is a full table
    // scan today; once an index exists on the column it becomes a single index probe.
    private static void EnforceUnique(Table table, Value[] row)
    {
        var columns = table.Schema.Columns;

        var checks = new List<(int Index, Value Value)>();
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Unique && !row[i].IsNull)
            {
                checks.Add((i, row[i]));
            }
        }

        if (checks.Count == 0)
        {
            return;
        }

        foreach (var (_, existing) in table.Heap.Scan())
        {
            foreach (var (index, value) in checks)
            {
                if (existing[index] == value)
                {
                    throw new OsqlException(
                        $"Duplicate value {value} for UNIQUE column '{columns[index].Name}'.");
                }
            }
        }
    }

    // ---- SELECT ----

    private ExecutionResult ExecuteSelect(SelectStatement statement)
    {
        var table = _catalog.Get(statement.TableName);
        var schema = table.Schema;

        int[] projection;
        string[] names;
        if (statement.IsStar)
        {
            projection = Enumerable.Range(0, schema.Columns.Count).ToArray();
            names = schema.Columns.Select(c => c.Name).ToArray();
        }
        else
        {
            projection = statement.Columns.Select(c => IndexOfColumn(schema, c)).ToArray();
            names = statement.Columns.ToArray();
        }

        var rows = new List<IReadOnlyList<Value>>();
        foreach (var (_, row) in table.Heap.Scan()) // seq scan through the buffer pool
        {
            if (!Matches(statement.Where, schema, row))
            {
                continue; // filter
            }

            var projected = new Value[projection.Length];
            for (var i = 0; i < projection.Length; i++)
            {
                projected[i] = row[projection[i]]; // project
            }

            rows.Add(projected);
        }

        return ExecutionResult.Query(new ResultSet(names, rows));
    }

    private static bool Matches(Expression? where, TableSchema schema, Value[] row)
    {
        if (where is null)
        {
            return true;
        }

        if (where is not BinaryExpression binary)
        {
            throw new OsqlException("Unsupported WHERE expression.");
        }

        var left = Evaluate(binary.Left, schema, row);
        var right = Evaluate(binary.Right, schema, row);
        return Compare(left, binary.Operator, right);
    }

    private static Value Evaluate(Expression expression, TableSchema schema, Value[] row) => expression switch
    {
        ColumnExpression column => row[IndexOfColumn(schema, column.Name)],
        LiteralExpression or NullLiteralExpression => LiteralToValue(expression),
        _ => throw new OsqlException("Unsupported expression in WHERE."),
    };

    private static bool Compare(Value left, ComparisonOperator op, Value right)
    {
        // SQL: a comparison involving NULL is never true.
        if (left.IsNull || right.IsNull)
        {
            return false;
        }

        int order;
        if (left.Type == DataType.Integer && right.Type == DataType.Integer)
        {
            order = left.AsInteger.CompareTo(right.AsInteger);
        }
        else if (left.Type == DataType.Text && right.Type == DataType.Text)
        {
            order = string.CompareOrdinal(left.AsText, right.AsText);
        }
        else
        {
            throw new OsqlException($"Cannot compare {left.Type} with {right.Type}.");
        }

        return op switch
        {
            ComparisonOperator.Equal => order == 0,
            ComparisonOperator.NotEqual => order != 0,
            ComparisonOperator.Less => order < 0,
            ComparisonOperator.LessEqual => order <= 0,
            ComparisonOperator.Greater => order > 0,
            ComparisonOperator.GreaterEqual => order >= 0,
            _ => throw new OsqlException($"Unsupported operator {op}."),
        };
    }

    // ---- recovery + shared helpers ----

    private void Replay()
    {
        foreach (var sql in _log.ReadAll())
        {
            try
            {
                if (Sql.Parse(sql) is CreateTableStatement create)
                {
                    RegisterTable(BuildSchema(create));
                }
                else
                {
                    throw new OsqlException($"unexpected {DescribeKind(Sql.Parse(sql))} in the DDL log");
                }
            }
            catch (OsqlException ex)
            {
                throw new LogCorruptionException($"Could not replay logged statement \"{sql}\": {ex.Message}");
            }
        }
    }

    private static Value LiteralToValue(Expression expression) => expression switch
    {
        LiteralExpression { Type: DataType.Integer } literal => Value.Integer((long)literal.Value),
        LiteralExpression { Type: DataType.Text } literal => Value.Text((string)literal.Value),
        NullLiteralExpression => Value.Null,
        _ => throw new OsqlException("Only literal values are allowed here."),
    };

    private static int IndexOfColumn(TableSchema schema, string name)
    {
        for (var i = 0; i < schema.Columns.Count; i++)
        {
            if (string.Equals(schema.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new OsqlException($"No such column '{name}' in table '{schema.Name}'.");
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

    public void Dispose()
    {
        foreach (var table in _catalog.Tables)
        {
            table.Dispose(); // flushes the heap's dirty pages and fsyncs
        }

        _log.Dispose();
    }
}
