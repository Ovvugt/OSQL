using OSQL.Core.Ast;
using OSQL.Core.Lexing;

namespace OSQL.Core.Parsing;

/// <summary>
/// A recursive-descent parser: one method per grammar rule. It consumes the token
/// list produced by the <see cref="Lexer"/> and builds an <see cref="SqlStatement"/>
/// AST. On malformed input it throws <see cref="SqlSyntaxException"/>.
/// </summary>
public sealed class Parser(IReadOnlyList<Token> tokens)
{
    private readonly IReadOnlyList<Token> _tokens = tokens;
    private int _position;

    /// <summary>Parse exactly one statement, allowing a trailing ';'.</summary>
    public SqlStatement ParseStatement()
    {
        var statement = ParseStatementBody();
        Match(TokenType.Semicolon); // optional terminator
        Expect(TokenType.EndOfFile, "end of input");
        return statement;
    }

    private SqlStatement ParseStatementBody()
    {
        return Current.Type switch
        {
            TokenType.Create => ParseCreate(),
            TokenType.Insert => ParseInsert(),
            TokenType.Select => ParseSelect(),
            _ => throw Error("a statement (CREATE, INSERT or SELECT)"),
        };
    }

    // ---- CREATE TABLE / CREATE INDEX ----

    private SqlStatement ParseCreate()
    {
        Expect(TokenType.Create, "CREATE");

        if (Match(TokenType.Table))
        {
            return ParseCreateTableBody();
        }

        if (Match(TokenType.Index))
        {
            return ParseCreateIndexBody();
        }

        throw Error("TABLE or INDEX after CREATE");
    }

    private CreateTableStatement ParseCreateTableBody()
    {
        var tableName = Expect(TokenType.Identifier, "a table name").Text;
        Expect(TokenType.LeftParen, "'('");

        var columns = new List<ColumnDefinition>();
        do
        {
            var name = Expect(TokenType.Identifier, "a column name").Text;
            var type = ParseDataType();
            var notNull = ParseOptionalNullability();
            columns.Add(new ColumnDefinition(name, type, notNull));
        }
        while (Match(TokenType.Comma));

        Expect(TokenType.RightParen, "')'");
        return new CreateTableStatement(tableName, columns);
    }

    private DataType ParseDataType()
    {
        if (Match(TokenType.Integer))
        {
            return DataType.Integer;
        }

        if (Match(TokenType.Text))
        {
            return DataType.Text;
        }

        throw Error("a column type (INTEGER or TEXT)");
    }

    // An optional nullability clause after a column's type: 'NOT NULL' makes the column
    // required; a bare type or an explicit 'NULL' leaves it nullable (the default).
    private bool ParseOptionalNullability()
    {
        if (Match(TokenType.Not))
        {
            Expect(TokenType.Null, "NULL after NOT");
            return true;
        }

        Match(TokenType.Null); // explicit NULL just restates the default
        return false;
    }

    private CreateIndexStatement ParseCreateIndexBody()
    {
        // CREATE INDEX [name] ON table (column) — the name is optional.
        string? indexName = null;
        if (!Check(TokenType.On))
        {
            indexName = Expect(TokenType.Identifier, "an index name or ON").Text;
        }

        Expect(TokenType.On, "ON");
        var tableName = Expect(TokenType.Identifier, "a table name").Text;
        Expect(TokenType.LeftParen, "'('");
        var columnName = Expect(TokenType.Identifier, "a column name").Text;
        Expect(TokenType.RightParen, "')'");

        return new CreateIndexStatement(indexName, tableName, columnName);
    }

    // ---- INSERT ----

    private InsertStatement ParseInsert()
    {
        Expect(TokenType.Insert, "INSERT");
        Expect(TokenType.Into, "INTO");
        var tableName = Expect(TokenType.Identifier, "a table name").Text;

        IReadOnlyList<string>? columns = null;
        if (Match(TokenType.LeftParen))
        {
            columns = ParseIdentifierList();
            Expect(TokenType.RightParen, "')'");
        }

        Expect(TokenType.Values, "VALUES");
        Expect(TokenType.LeftParen, "'('");
        var values = ParseValueList();
        Expect(TokenType.RightParen, "')'");

        return new InsertStatement(tableName, columns, values);
    }

    private List<string> ParseIdentifierList()
    {
        var names = new List<string>();
        do
        {
            names.Add(Expect(TokenType.Identifier, "an identifier").Text);
        }
        while (Match(TokenType.Comma));

        return names;
    }

    private List<Expression> ParseValueList()
    {
        var values = new List<Expression>();
        do
        {
            values.Add(ParseLiteral());
        }
        while (Match(TokenType.Comma));

        return values;
    }

    // ---- SELECT ----

    private SelectStatement ParseSelect()
    {
        Expect(TokenType.Select, "SELECT");

        var isStar = false;
        var columns = new List<string>();
        if (Match(TokenType.Asterisk))
        {
            isStar = true;
        }
        else
        {
            columns = ParseIdentifierList();
        }

        Expect(TokenType.From, "FROM");
        var tableName = Expect(TokenType.Identifier, "a table name").Text;

        Expression? where = null;
        if (Match(TokenType.Where))
        {
            where = ParsePredicate();
        }

        return new SelectStatement(isStar, columns, tableName, where);
    }

    // ---- Expressions ----

    private Expression ParsePredicate()
    {
        var left = ParseOperand();
        var op = ParseComparisonOperator();
        var right = ParseOperand();
        return new BinaryExpression(left, op, right);
    }

    private Expression ParseOperand()
    {
        if (Check(TokenType.Identifier))
        {
            return new ColumnExpression(Advance().Text);
        }

        return ParseLiteral();
    }

    private Expression ParseLiteral()
    {
        if (Check(TokenType.NumberLiteral))
        {
            var token = Advance();
            return new LiteralExpression(ParseInteger(token), DataType.Integer);
        }

        if (Check(TokenType.StringLiteral))
        {
            return new LiteralExpression(Advance().Text, DataType.Text);
        }

        if (Match(TokenType.Null))
        {
            return new NullLiteralExpression();
        }

        throw Error("a literal value");
    }

    private ComparisonOperator ParseComparisonOperator()
    {
        var token = Advance();
        return token.Type switch
        {
            TokenType.Equal => ComparisonOperator.Equal,
            TokenType.NotEqual => ComparisonOperator.NotEqual,
            TokenType.Less => ComparisonOperator.Less,
            TokenType.LessEqual => ComparisonOperator.LessEqual,
            TokenType.Greater => ComparisonOperator.Greater,
            TokenType.GreaterEqual => ComparisonOperator.GreaterEqual,
            _ => throw new SqlSyntaxException(
                $"Expected a comparison operator but found {Describe(token)} at position {token.Position}."),
        };
    }

    private static long ParseInteger(Token token)
    {
        if (!long.TryParse(token.Text, out var value))
        {
            throw new SqlSyntaxException($"Invalid integer '{token.Text}' at position {token.Position}.");
        }

        return value;
    }

    // ---- Token cursor helpers ----

    private Token Current => _tokens[_position];

    private bool IsAtEnd => Current.Type == TokenType.EndOfFile;

    private Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _position++;
        }

        return token;
    }

    private bool Check(TokenType type)
    {
        return Current.Type == type;
    }

    private bool Match(TokenType type)
    {
        if (!Check(type))
        {
            return false;
        }

        _position++;
        return true;
    }

    private Token Expect(TokenType type, string description)
    {
        if (!Check(type))
        {
            throw Error(description);
        }

        return Advance();
    }

    private SqlSyntaxException Error(string expected)
    {
        var token = Current;
        return new SqlSyntaxException($"Expected {expected} but found {Describe(token)} at position {token.Position}.");
    }

    /// <summary>
    /// Render a token for an error message, naming its kind so a quoted value isn't mistaken
    /// for a bare word: a string literal reads as <c>string 'x'</c>, a number as <c>number 5</c>.
    /// </summary>
    private static string Describe(Token token) => token.Type switch
    {
        TokenType.EndOfFile => "end of input",
        TokenType.StringLiteral => $"string '{token.Text}'",
        TokenType.NumberLiteral => $"number {token.Text}",
        _ => $"'{token.Text}'",
    };
}
