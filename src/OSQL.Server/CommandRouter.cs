using OSQL.Core;
using OSQL.Core.Storage;

namespace OSQL.Server;

/// <summary>
/// Turns a request string into a response string by running it through the database:
/// lex → parse → execute. A successful statement comes back as an <c>OK:</c> line; an
/// <see cref="OsqlException"/> (bad syntax, a broken schema rule) comes back as an
/// <c>ERROR:</c> line. The database itself owns durability and the writer lock, so this
/// router is a thin, stateless seam over it.
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
            response = $"OK: {_database.Execute(request)}";
        }
        catch (OsqlException ex)
        {
            response = $"ERROR: {ex.Message}";
        }

        return Task.FromResult(response);
    }
}
