using System.Net.Sockets;
using OSQL.Wire;

namespace OSQL.Client;

/// <summary>
/// A thin client connection to an OSQL server: connect, send a request, read the
/// one reply that comes back. Owns the underlying socket and stream.
/// </summary>
public sealed class OsqlConnection : IAsyncDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _client.ConnectAsync(host, port, ct);
        _stream = _client.GetStream();
    }

    /// <summary>
    /// Send a request and return the server's reply, or <c>null</c> if the server
    /// closed the connection.
    /// </summary>
    public async Task<string?> SendAsync(string message, CancellationToken ct = default)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Connect before sending.");
        }

        await FrameCodec.WriteTextAsync(_stream, message, ct);
        var reply = await FrameCodec.ReadFrameAsync(_stream, ct);
        return reply?.AsText();
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }
        _client.Dispose();
    }
}
