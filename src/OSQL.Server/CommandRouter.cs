namespace OSQL.Server;

/// <summary>
/// Turns a request string into a response string. For now it simply echoes the
/// request back so we can verify the end-to-end communication pipeline. This is
/// the seam where the SQL pipeline (lex -> parse -> plan -> execute) will plug in:
/// route the SQL here and return result rows or an error.
/// </summary>
public sealed class CommandRouter
{
    public Task<string> RouteAsync(string request, CancellationToken ct = default)
    {
        // Echo the request back unchanged to confirm the round-trip works.
        return Task.FromResult(request);
    }
}
