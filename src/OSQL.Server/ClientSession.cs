using System.Net;
using System.Net.Sockets;
using OSQL.Wire;

namespace OSQL.Server;

/// <summary>
/// Handles the lifetime of a single client connection: read a frame, route it to
/// a response, write the response, repeat until the client disconnects.
/// </summary>
public sealed class ClientSession(TcpClient client, CommandRouter router)
{
    private readonly TcpClient _client = client;
    private readonly CommandRouter _router = router;

    public async Task RunAsync(CancellationToken ct)
    {
        var remote = _client.Client.RemoteEndPoint;
        Console.WriteLine($"Client connected: {remote}");

        try
        {
            using (_client)
            await using (var stream = _client.GetStream())
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadFrameAsync(stream, ct);
                    if (frame is null)
                    {
                        break; // client closed the connection
                    }

                    var request = frame.Value.AsText();
                    var response = await _router.RouteAsync(request, ct);

                    Console.WriteLine($"{remote} -> {request.Trim()} | <- {response}");
                    await FrameCodec.WriteTextAsync(stream, response, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server is shutting down; nothing to report.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection {remote} ended with error: {ex.Message}");
        }
        finally
        {
            Console.WriteLine($"Client disconnected: {remote}");
        }
    }
}
