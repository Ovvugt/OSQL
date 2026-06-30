using OSQL.Core.Ast;

namespace OSQL.Core.Storage;

/// <summary>
/// The shape of one table: its name and its ordered columns. This is the catalog's
/// record of a table, built from a validated <see cref="CreateTableStatement"/>.
/// </summary>
public sealed record TableSchema(string Name, IReadOnlyList<ColumnDefinition> Columns);
