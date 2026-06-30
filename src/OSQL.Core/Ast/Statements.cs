namespace OSQL.Core.Ast;

/// <summary>Base type for every parsed top-level statement.</summary>
public abstract record SqlStatement;

/// <summary>The column types OSQL supports for now.</summary>
public enum DataType
{
    Integer,
    Text,
}

/// <summary>How a column's value is produced when a row doesn't supply one.</summary>
public enum ColumnGeneration
{
    /// <summary>Not generated; the row supplies the value (or it stays NULL).</summary>
    None,

    /// <summary>An auto-incrementing INTEGER, declared as <c>SERIAL</c>.</summary>
    Serial,
}

/// <summary>
/// A column declaration inside a CREATE TABLE statement. <see cref="NotNull"/> forbids
/// storing a NULL; <see cref="Unique"/> forbids two rows sharing the same non-NULL value;
/// <see cref="Generated"/> says whether (and how) the database fills the value in itself.
/// </summary>
public sealed record ColumnDefinition(
    string Name,
    DataType Type,
    bool NotNull = false,
    bool Unique = false,
    ColumnGeneration Generated = ColumnGeneration.None);

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
