using OSQL.Wire;

namespace OSQL.Client;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : WireProtocol.DefaultPort;

        await using var connection = new OsqlConnection();
        await connection.ConnectAsync("127.0.0.1", port);
        Console.WriteLine($"Connected to OSQL server on port {port}. Type a message (e.g. 'ping'), blank line to quit.");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var reply = await connection.SendAsync(line);
            if (reply is null)
            {
                Console.WriteLine("Server closed the connection.");
                break;
            }

            Console.WriteLine(reply);
        }

        Console.WriteLine("Bye.");
    }
}
