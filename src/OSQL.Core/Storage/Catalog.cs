namespace OSQL.Core.Storage;

/// <summary>
/// The database's in-memory metadata about itself: which tables exist and their shape.
/// Names are matched case-insensitively (so <c>Users</c> and <c>users</c> are the same
/// table) while their original spelling is preserved for display. This is pure state;
/// durability and locking live one layer up in <see cref="Database"/>.
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, TableSchema> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True if a table with this name (any casing) already exists.</summary>
    public bool Contains(string name) => _tables.ContainsKey(name);

    /// <summary>Every table currently known, in no particular order.</summary>
    public IReadOnlyCollection<TableSchema> Tables => _tables.Values;

    /// <summary>Look up a table, or throw if there is no such table.</summary>
    public TableSchema Get(string name) =>
        _tables.TryGetValue(name, out var schema)
            ? schema
            : throw new OsqlException($"No such table '{name}'.");

    /// <summary>Register a new table. Throws if the name is already taken.</summary>
    public void Add(TableSchema schema)
    {
        if (!_tables.TryAdd(schema.Name, schema))
        {
            throw new OsqlException($"Table '{schema.Name}' already exists.");
        }
    }
}
