namespace OSQL.Core.Ast;

/// <summary>Base type for every parsed top-level statement.</summary>
public abstract record SqlStatement;

/// <summary>The column types OSQL supports for now.</summary>
public enum DataType
{
    Integer,
    Text,
}

/// <summary>
/// A column declaration inside a CREATE TABLE statement. <see cref="NotNull"/> is true
/// when the column was declared <c>NOT NULL</c>, which forbids storing a NULL in it.
/// </summary>
public sealed record ColumnDefinition(string Name, DataType Type, bool NotNull = false);

/// <summary><c>CREATE TABLE name (col TYPE, ...)</c></summary>
public sealed record CreateTableStatement(
    string TableName,
    IReadOnlyList<ColumnDefinition> Columns) : SqlStatement;

/// <summary>
/// <c>INSERT INTO name [(col, ...)] VALUES (v, ...)</c>.
/// <see cref="Columns"/> is null when no explicit column list was given.
/// </summary>
public sealed record InsertStatement(
    string TableName,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<Expression> Values) : SqlStatement;

/// <summary>
/// <c>SELECT (* | col, ...) FROM name [WHERE predicate]</c>.
/// When <see cref="IsStar"/> is true, <see cref="Columns"/> is empty.
/// </summary>
public sealed record SelectStatement(
    bool IsStar,
    IReadOnlyList<string> Columns,
    string TableName,
    Expression? Where) : SqlStatement;

/// <summary>
/// <c>CREATE INDEX [name] ON table (column)</c>.
/// <see cref="IndexName"/> is null when the index is unnamed.
/// </summary>
public sealed record CreateIndexStatement(
    string? IndexName,
    string TableName,
    string ColumnName) : SqlStatement;
