namespace OSQL.Client;

/// <summary>Outcome of trying to handle a line as a client meta-command.</summary>
internal enum MetaCommandResult
{
    /// <summary>Not a meta-command; treat the line as statement input.</summary>
    NotHandled,

    /// <summary>Handled locally; carry on with the REPL.</summary>
    Handled,

    /// <summary>The client should disconnect.</summary>
    Quit,
}

/// <summary>
/// Client-side meta-commands, psql-style. They start with a backslash (e.g. <c>\q</c>)
/// so they can never collide with SQL keywords. Anything that isn't a meta-command is
/// sent to the server as a statement. This is where future commands like <c>\dt</c>
/// (list tables) or <c>\d</c> (describe) will live.
/// </summary>
internal static class MetaCommands
{
    public static MetaCommandResult TryExecute(string line)
    {
        var command = line.Trim();
        if (command.Length == 0)
        {
            return MetaCommandResult.NotHandled;
        }

        // Bare-word aliases for \q, the way psql tolerates "exit"/"quit".
        if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return MetaCommandResult.Quit;
        }

        // Everything else local must be backslash-prefixed.
        if (command[0] != '\\')
        {
            return MetaCommandResult.NotHandled;
        }

        switch (command)
        {
            case "\\q":
                return MetaCommandResult.Quit;

            case "\\?":
                PrintHelp();
                return MetaCommandResult.Handled;

            default:
                Console.WriteLine($"Unknown command: {command}. Type \\? for help.");
                return MetaCommandResult.Handled;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Client commands:");
        Console.WriteLine("  \\q            quit (aliases: exit, quit, Ctrl+D)");
        Console.WriteLine("  \\?            show this help");
        Console.WriteLine();
        Console.WriteLine("Anything else is sent to the server; end a statement with ';'.");
        Console.WriteLine("Press Ctrl+C to clear the statement you're typing.");
    }
}
