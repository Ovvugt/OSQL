namespace OSQL.Core.Ast;

/// <summary>Base type for value expressions (currently only used in WHERE).</summary>
public abstract record Expression;

/// <summary>A reference to a column by name, e.g. <c>age</c>.</summary>
public sealed record ColumnExpression(string Name) : Expression;

/// <summary>
/// A literal value. <see cref="Value"/> is a <see cref="long"/> for
/// <see cref="DataType.Integer"/> and a <see cref="string"/> for
/// <see cref="DataType.Text"/>.
/// </summary>
public sealed record LiteralExpression(object Value, DataType Type) : Expression;

/// <summary>The literal <c>NULL</c> — a value of no particular type.</summary>
public sealed record NullLiteralExpression : Expression;

/// <summary>A comparison such as <c>age &gt; 30</c>.</summary>
public sealed record BinaryExpression(
    Expression Left,
    ComparisonOperator Operator,
    Expression Right) : Expression;

/// <summary>The comparison operators allowed in a WHERE predicate.</summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}
