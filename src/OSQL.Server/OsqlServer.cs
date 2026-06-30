using System.Net;
using System.Net.Sockets;

namespace OSQL.Server;

/// <summary>
/// Owns the TCP listener and the accept loop. Each accepted connection is handed
/// to its own <see cref="ClientSession"/> so connections are handled independently.
/// </summary>
public sealed class OsqlServer(IPEndPoint endpoint, CommandRouter router)
{
    private readonly IPEndPoint _endpoint = endpoint;
    private readonly CommandRouter _router = router;

    /// <summary>Listen and accept connections until the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_endpoint);
        listener.Start();
        Console.WriteLine($"OSQL server listening on {listener.LocalEndpoint}. Press Ctrl+C to stop.");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                // Fire-and-forget the session: one slow or broken client must not
                // block others. Writes get serialized later by the storage layer.
                _ = new ClientSession(client, _router).RunAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown.
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("OSQL server stopped.");
        }
    }
}
