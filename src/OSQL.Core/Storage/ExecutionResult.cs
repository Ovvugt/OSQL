namespace OSQL.Core.Storage;

/// <summary>A SELECT result: the projected column names and the rows beneath them.</summary>
public sealed record ResultSet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<Value>> Rows);

/// <summary>
/// The outcome of executing one statement: either a short status <see cref="Message"/>
/// (for CREATE TABLE / INSERT) or a <see cref="Rows"/> result set (for SELECT). Turning
/// rows into display text is the caller's job, which keeps the engine free of presentation.
/// </summary>
public sealed record ExecutionResult
{
    private ExecutionResult(string? message, ResultSet? rows)
    {
        Message = message;
        Rows = rows;
    }

    public string? Message { get; }
    public ResultSet? Rows { get; }

    public static ExecutionResult Status(string message) => new(message, null);
    public static ExecutionResult Query(ResultSet rows) => new(null, rows);
}
