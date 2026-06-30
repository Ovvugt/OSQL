namespace OSQL.Core.Storage;

/// <summary>
/// The database's in-memory registry of its tables: name → <see cref="Table"/> (schema
/// plus the open heap file). Names are matched case-insensitively while their original
/// spelling is preserved. Durability and locking live one layer up in <see cref="Database"/>.
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, Table> _tables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True if a table with this name (any casing) already exists.</summary>
    public bool Contains(string name) => _tables.ContainsKey(name);

    /// <summary>Every table currently known.</summary>
    public IReadOnlyCollection<Table> Tables => _tables.Values;

    /// <summary>Look up a table, or throw if there is no such table.</summary>
    public Table Get(string name) =>
        _tables.TryGetValue(name, out var table)
            ? table
            : throw new OsqlException($"No such table '{name}'.");

    /// <summary>Register a new table. Throws if the name is already taken.</summary>
    public void Add(Table table)
    {
        if (!_tables.TryAdd(table.Name, table))
        {
            throw new OsqlException($"Table '{table.Name}' already exists.");
        }
    }
}
