namespace OSQL.Core.Lexing;

/// <summary>
/// A single lexical token: its kind, the source text it came from, and the start
/// position in the input (used for error messages). For string literals, <see
/// cref="Text"/> holds the unescaped content without the surrounding quotes.
/// </summary>
public readonly record struct Token(TokenType Type, string Text, int Position)
{
    public override string ToString()
    {
        return $"{Type}('{Text}')";
    }
}
