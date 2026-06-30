using OSQL.Wire;

namespace OSQL.Client;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : WireProtocol.DefaultPort;

        // Capture Ctrl+C as input (so it can cancel a statement) instead of killing
        // the process. Only meaningful on an interactive console.
        if (!Console.IsInputRedirected)
        {
            Console.TreatControlCAsInput = true;
        }

        await using var connection = new OsqlConnection();
        await connection.ConnectAsync("127.0.0.1", port);
        Console.WriteLine(
            $"Connected to OSQL server on port {port}. " +
            "End a statement with ';'. Type \\? for help, \\q to quit.");

        var editor = new StatementEditor(IsComplete);
        while (true)
        {
            var read = editor.ReadStatement();
            if (read.Result == LineResult.Quit)
            {
                break;
            }

            if (read.Result == LineResult.Cancel)
            {
                continue; // statement abandoned; the editor already moved on
            }

            var input = read.Text.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            // Client meta-commands (\q, \?, exit, quit) are handled locally.
            var meta = MetaCommands.TryExecute(input);
            if (meta == MetaCommandResult.Quit)
            {
                break;
            }

            if (meta == MetaCommandResult.Handled)
            {
                continue;
            }

            var command = input.EndsWith(';') ? input[..^1].Trim() : input;
            if (command.Length == 0)
            {
                continue;
            }

            var reply = await connection.SendAsync(command);
            if (reply is null)
            {
                Console.WriteLine("Server closed the connection.");
                break;
            }

            Console.WriteLine(reply);
        }

        Console.WriteLine("Disconnected.");
    }

    /// <summary>
    /// Decide whether Enter (at the end of input) submits the buffer or inserts a
    /// new line. We submit on an empty buffer (ignored upstream), a ';'-terminated
    /// statement, or a client meta-command; otherwise Enter continues onto a new line.
    /// </summary>
    private static bool IsComplete(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0
            || trimmed.EndsWith(';')
            || trimmed.StartsWith('\\')
            || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }
}
