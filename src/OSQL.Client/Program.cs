using System.Text;
using OSQL.Wire;

namespace OSQL.Client;

internal static class Program
{
    private const string ReadyPrompt = "osql=> ";
    private const string ContinuationPrompt = "osql-> ";

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

        // Lines accumulate here until the statement is terminated with ';'.
        var statement = new StringBuilder();
        while (true)
        {
            Console.Write(statement.Length == 0 ? ReadyPrompt : ContinuationPrompt);

            var read = ReadCommand();
            if (read.Result == LineResult.Quit)
            {
                Console.WriteLine();
                break;
            }

            if (read.Result == LineResult.Cancel)
            {
                Console.WriteLine();
                statement.Clear();
                continue;
            }

            // Meta-commands are only recognised at the start of a statement, so a
            // word like 'exit' inside a multi-line statement isn't hijacked.
            if (statement.Length == 0)
            {
                var meta = MetaCommands.TryExecute(read.Text);
                if (meta == MetaCommandResult.Quit)
                {
                    break;
                }

                if (meta == MetaCommandResult.Handled)
                {
                    continue;
                }
            }

            statement.AppendLine(read.Text);

            // The statement is complete once its text ends with the ';' terminator.
            var text = statement.ToString().TrimEnd();
            if (!text.EndsWith(';'))
            {
                continue;
            }

            statement.Clear();

            var command = text[..^1].Trim(); // drop the ';' terminator
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
    /// Read one line of input. On an interactive console we read key by key so we
    /// can catch Ctrl+D (quit) and Ctrl+C (cancel the statement). When input is
    /// redirected (e.g. a test pipe) we fall back to <see cref="Console.ReadLine"/>,
    /// where end-of-input maps to quit.
    /// </summary>
    private static ConsoleLine ReadCommand()
    {
        if (Console.IsInputRedirected)
        {
            var piped = Console.ReadLine();
            return piped is null ? ConsoleLine.Quit : ConsoleLine.FromText(piped);
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (key.Key == ConsoleKey.D)
                {
                    return ConsoleLine.Quit;
                }

                if (key.Key == ConsoleKey.C)
                {
                    return ConsoleLine.Cancel;
                }
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return ConsoleLine.FromText(buffer.ToString());

                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b"); // erase the last character on screen
                    }

                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                        Console.Write(key.KeyChar);
                    }

                    break;
            }
        }
    }
}
