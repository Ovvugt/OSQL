namespace OSQL.Client;

/// <summary>What a single read from the console produced.</summary>
internal enum LineResult
{
    /// <summary>A line of text was entered (see <see cref="ConsoleLine.Text"/>).</summary>
    Line,

    /// <summary>Ctrl+C: abandon the statement currently being typed.</summary>
    Cancel,

    /// <summary>Ctrl+D or end-of-input: disconnect.</summary>
    Quit,
}

/// <summary>The result of reading one line of console input.</summary>
internal readonly record struct ConsoleLine(LineResult Result, string Text)
{
    public static readonly ConsoleLine Quit = new(LineResult.Quit, string.Empty);
    public static readonly ConsoleLine Cancel = new(LineResult.Cancel, string.Empty);

    public static ConsoleLine FromText(string text)
    {
        return new ConsoleLine(LineResult.Line, text);
    }
}
