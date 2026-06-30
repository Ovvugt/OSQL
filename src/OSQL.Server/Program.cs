using System.Net;
using OSQL.Wire;

namespace OSQL.Server;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : WireProtocol.DefaultPort;

        // Ctrl+C requests a graceful shutdown rather than killing the process.
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        var server = new OsqlServer(new IPEndPoint(IPAddress.Loopback, port), new CommandRouter());
        await server.RunAsync(shutdown.Token);
    }
}
