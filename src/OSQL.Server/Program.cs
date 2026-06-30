using System.Net;
using OSQL.Core.Storage;
using OSQL.Wire;

namespace OSQL.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : WireProtocol.DefaultPort;

        // The data directory defaults to ./data next to the server. OSQL_DATA overrides
        // it (PostgreSQL's PGDATA pattern), which is how a future version would point a
        // new build at an existing database.
        var dataDirectory = Environment.GetEnvironmentVariable("OSQL_DATA") ?? "data";

        Database database;
        try
        {
            database = Database.Open(dataDirectory);
        }
        catch (Exception ex) when (ex is StorageFormatException or LogCorruptionException)
        {
            // Refusing to start beats serving from data we can't trust.
            Console.Error.WriteLine($"Cannot open data directory: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Data directory: {Path.GetFullPath(dataDirectory)} "
            + $"({database.Tables.Count} table(s) restored).");

        // Ctrl+C requests a graceful shutdown rather than killing the process.
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        using (database)
        {
            var server = new OsqlServer(new IPEndPoint(IPAddress.Loopback, port), new CommandRouter(database));
            await server.RunAsync(shutdown.Token);
        }

        return 0;
    }
}
