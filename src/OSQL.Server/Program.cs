using System.Net;
using OSQL.Core.Storage;
using OSQL.Wire;

namespace OSQL.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : WireProtocol.DefaultPort;

        var dataDirectory = ResolveDataDirectory();

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

    /// <summary>
    /// Decide where the database lives. <c>OSQL_DATA</c> wins (PostgreSQL's PGDATA pattern).
    /// Otherwise, in a dev checkout we anchor to <c>&lt;repo&gt;/data</c> so cleaning or rebuilding
    /// the build output doesn't wipe the database; an installed build (no solution file up the
    /// tree) falls back to <c>./data</c>, the <c>INSTALL_DIR/osql/&lt;version&gt;</c> layout.
    /// </summary>
    private static string ResolveDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("OSQL_DATA");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return repositoryRoot is null ? "data" : Path.Combine(repositoryRoot, "data");
    }

    /// <summary>Walk up from <paramref name="start"/> looking for the solution file.</summary>
    private static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OSQL.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
