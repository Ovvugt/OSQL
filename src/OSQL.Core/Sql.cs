using OSQL.Core.Ast;
using OSQL.Core.Lexing;
using OSQL.Core.Parsing;

namespace OSQL.Core;

/// <summary>Convenience entry point: lex and parse a single SQL statement.</summary>
public static class Sql
{
    public static SqlStatement Parse(string text)
    {
        var tokens = new Lexer(text).Tokenize();
        return new Parser(tokens).ParseStatement();
    }
}
