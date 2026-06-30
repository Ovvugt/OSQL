using OSQL.Core;
using OSQL.Core.Ast;

namespace OSQL.Server;

/// <summary>
/// Turns a request string into a response string. It runs the SQL front-end
/// (lex -> parse) over the request and reports back what it understood: an
/// <c>OK:</c> line summarising the parsed statement, or an <c>ERROR:</c> line
/// carrying the syntax error. This is the seam where the rest of the pipeline
/// (plan -> execute) will plug in once statements actually run.
/// </summary>
public sealed class CommandRouter
{
    public Task<string> RouteAsync(string request, CancellationToken ct = default)
    {
        // Nothing to do for a blank line; keep the round-trip quiet.
        if (string.IsNullOrWhiteSpace(request))
        {
            return Task.FromResult(string.Empty);
        }

        string response;
        try
        {
            var statement = Sql.Parse(request);
            response = $"OK: {Describe(statement)}";
        }
        catch (SqlSyntaxException ex)
        {
            response = $"ERROR: {ex.Message}";
        }

        return Task.FromResult(response);
    }

    /// <summary>Render a parsed statement as a compact, human-readable one-liner.</summary>
    private static string Describe(SqlStatement statement) => statement switch
    {
        CreateTableStatement s =>
            $"CreateTable '{s.TableName}' ({string.Join(", ", s.Columns.Select(DescribeColumn))})",
        InsertStatement s =>
            $"Insert into '{s.TableName}'{DescribeColumnList(s.Columns)} values ({string.Join(", ", s.Values.Select(Describe))})",
        SelectStatement s =>
            $"Select {(s.IsStar ? "*" : string.Join(", ", s.Columns))} from '{s.TableName}'{DescribeWhere(s.Where)}",
        CreateIndexStatement s =>
            $"CreateIndex {(s.IndexName is null ? "" : $"'{s.IndexName}' ")}on '{s.TableName}' ({s.ColumnName})",
        _ => statement.GetType().Name,
    };

    private static string DescribeColumn(ColumnDefinition column) =>
        $"{column.Name} {DescribeType(column.Type)}";

    private static string DescribeColumnList(IReadOnlyList<string>? columns) =>
        columns is null ? "" : $" ({string.Join(", ", columns)})";

    private static string DescribeWhere(Expression? where) =>
        where is null ? "" : $" where {Describe(where)}";

    private static string Describe(Expression expression) => expression switch
    {
        ColumnExpression e => e.Name,
        LiteralExpression { Type: DataType.Text } e => $"'{e.Value}'",
        LiteralExpression e => e.Value.ToString() ?? "",
        BinaryExpression e => $"{Describe(e.Left)} {DescribeOperator(e.Operator)} {Describe(e.Right)}",
        _ => expression.GetType().Name,
    };

    private static string DescribeType(DataType type) => type switch
    {
        DataType.Integer => "INTEGER",
        DataType.Text => "TEXT",
        _ => type.ToString(),
    };

    private static string DescribeOperator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "<>",
        ComparisonOperator.Less => "<",
        ComparisonOperator.LessEqual => "<=",
        ComparisonOperator.Greater => ">",
        ComparisonOperator.GreaterEqual => ">=",
        _ => op.ToString(),
    };
}
