using System.Text;

namespace OSQL.Core.Lexing;

/// <summary>
/// Turns SQL text into a flat list of <see cref="Token"/>s. A character-by-character
/// scanner: it skips whitespace, groups letters/digits into identifiers, keywords
/// and numbers, reads quoted strings, and recognises operators and punctuation.
/// Keyword matching is case-insensitive; identifier text is preserved as written.
/// </summary>
public sealed class Lexer(string source)
{
    private static readonly IReadOnlyDictionary<string, TokenType> _keywords =
        new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase)
        {
            ["CREATE"] = TokenType.Create,
            ["TABLE"] = TokenType.Table,
            ["INDEX"] = TokenType.Index,
            ["INSERT"] = TokenType.Insert,
            ["INTO"] = TokenType.Into,
            ["VALUES"] = TokenType.Values,
            ["SELECT"] = TokenType.Select,
            ["FROM"] = TokenType.From,
            ["WHERE"] = TokenType.Where,
            ["ON"] = TokenType.On,
            ["INTEGER"] = TokenType.Integer,
            ["TEXT"] = TokenType.Text,
        };

    private readonly string _source = source;
    private int _position;

    /// <summary>Scan the whole input, ending with an <see cref="TokenType.EndOfFile"/> token.</summary>
    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var token = NextToken();
            tokens.Add(token);
            if (token.Type == TokenType.EndOfFile)
            {
                return tokens;
            }
        }
    }

    private bool IsAtEnd => _position >= _source.Length;

    private Token NextToken()
    {
        SkipWhitespace();
        if (IsAtEnd)
        {
            return new Token(TokenType.EndOfFile, string.Empty, _position);
        }

        var start = _position;
        var c = _source[_position];

        if (char.IsLetter(c) || c == '_')
        {
            return ReadIdentifierOrKeyword(start);
        }

        if (char.IsDigit(c))
        {
            return ReadNumber(start);
        }

        if (c == '\'')
        {
            return ReadString(start);
        }

        return ReadOperatorOrPunctuation(start);
    }

    private void SkipWhitespace()
    {
        while (!IsAtEnd && char.IsWhiteSpace(_source[_position]))
        {
            _position++;
        }
    }

    private Token ReadIdentifierOrKeyword(int start)
    {
        while (!IsAtEnd && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_'))
        {
            _position++;
        }

        var text = _source[start.._position];
        var type = _keywords.TryGetValue(text, out var keyword) ? keyword : TokenType.Identifier;
        return new Token(type, text, start);
    }

    private Token ReadNumber(int start)
    {
        while (!IsAtEnd && char.IsDigit(_source[_position]))
        {
            _position++;
        }

        return new Token(TokenType.NumberLiteral, _source[start.._position], start);
    }

    private Token ReadString(int start)
    {
        _position++; // consume the opening quote
        var value = new StringBuilder();
        while (true)
        {
            if (IsAtEnd)
            {
                throw new SqlSyntaxException($"Unterminated string literal at position {start}.");
            }

            var c = _source[_position];
            _position++;

            if (c == '\'')
            {
                // Two single quotes in a row is an escaped quote, not the end.
                if (!IsAtEnd && _source[_position] == '\'')
                {
                    value.Append('\'');
                    _position++;
                    continue;
                }

                break;
            }

            value.Append(c);
        }

        return new Token(TokenType.StringLiteral, value.ToString(), start);
    }

    private Token ReadOperatorOrPunctuation(int start)
    {
        var c = _source[_position];
        _position++;

        switch (c)
        {
            case ',':
                return new Token(TokenType.Comma, ",", start);
            case '(':
                return new Token(TokenType.LeftParen, "(", start);
            case ')':
                return new Token(TokenType.RightParen, ")", start);
            case '*':
                return new Token(TokenType.Asterisk, "*", start);
            case ';':
                return new Token(TokenType.Semicolon, ";", start);
            case '=':
                return new Token(TokenType.Equal, "=", start);
            case '<':
                if (Match('='))
                {
                    return new Token(TokenType.LessEqual, "<=", start);
                }

                if (Match('>'))
                {
                    return new Token(TokenType.NotEqual, "<>", start);
                }

                return new Token(TokenType.Less, "<", start);
            case '>':
                if (Match('='))
                {
                    return new Token(TokenType.GreaterEqual, ">=", start);
                }

                return new Token(TokenType.Greater, ">", start);
            case '!':
                if (Match('='))
                {
                    return new Token(TokenType.NotEqual, "!=", start);
                }

                throw new SqlSyntaxException($"Unexpected character '!' at position {start}.");
            default:
                throw new SqlSyntaxException($"Unexpected character '{c}' at position {start}.");
        }
    }

    private bool Match(char expected)
    {
        if (IsAtEnd || _source[_position] != expected)
        {
            return false;
        }

        _position++;
        return true;
    }
}
