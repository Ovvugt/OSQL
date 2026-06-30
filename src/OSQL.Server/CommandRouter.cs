using System.Text;
using OSQL.Core;
using OSQL.Core.Ast;
using OSQL.Core.Storage;

namespace OSQL.Server;

/// <summary>
/// Turns a request string into a response string by running it through the database and
/// rendering the outcome: a status line for CREATE TABLE / INSERT, an aligned table for a
/// SELECT result set, or an <c>ERROR:</c> line for an <see cref="OsqlException"/>. The
/// engine returns structured results; the display formatting lives here, in the server.
/// </summary>
public sealed class CommandRouter(Database database)
{
    private readonly Database _database = database;

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
            var result = _database.Execute(request);
            response = result.Rows is not null
                ? FormatTable(result.Rows)
                : result.Message ?? string.Empty;
        }
        catch (OsqlException ex)
        {
            response = $"ERROR: {ex.Message}";
        }

        return Task.FromResult(response);
    }

    private static string FormatTable(ResultSet result)
    {
        var columns = result.Columns;
        var widths = new int[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            widths[c] = columns[c].Length;
        }

        var renderedRows = new List<string[]>();
        foreach (var row in result.Rows)
        {
            var cells = new string[columns.Count];
            for (var c = 0; c < columns.Count; c++)
            {
                cells[c] = RenderCell(row[c]);
                if (cells[c].Length > widths[c])
                {
                    widths[c] = cells[c].Length;
                }
            }

            renderedRows.Add(cells);
        }

        var builder = new StringBuilder();
        AppendRow(builder, columns.ToArray(), widths);
        builder.AppendLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (var cells in renderedRows)
        {
            AppendRow(builder, cells, widths);
        }

        builder.Append($"({renderedRows.Count} {(renderedRows.Count == 1 ? "row" : "rows")})");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string[] cells, int[] widths)
    {
        for (var c = 0; c < cells.Length; c++)
        {
            if (c > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(cells[c].PadRight(widths[c]));
        }

        builder.Append('\n');
    }

    private static string RenderCell(Value value)
    {
        if (value.IsNull)
        {
            return "NULL";
        }

        return value.Type == DataType.Integer ? value.AsInteger.ToString() : value.AsText;
    }
}
