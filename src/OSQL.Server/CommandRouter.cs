namespace OSQL.Server;

/// <summary>
/// Turns a request string into a response string. For milestone M0 it only knows
/// "ping". This is the seam where the SQL pipeline (lex -> parse -> plan ->
/// execute) will plug in: route SQL here and return result rows or an error.
/// </summary>
public sealed class CommandRouter
{
    public Task<string> RouteAsync(string request, CancellationToken ct = default)
    {
        var command = request.Trim();

        var response = command.Equals("ping", StringComparison.OrdinalIgnoreCase)
            ? "pong"
            : $"unknown command: {command}";

        return Task.FromResult(response);
    }
}
