using OSQL.Core;
using OSQL.Core.Lexing;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class LexerTests
{
    private static IReadOnlyList<Token> Tokenize(string source) => new Lexer(source).Tokenize();

    private static IReadOnlyList<TokenType> TypesOf(string source) =>
        Tokenize(source).Select(t => t.Type).ToList();

    [Test]
    public void Tokenize_AlwaysEndsWithEndOfFile()
    {
        var tokens = Tokenize("");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[^1].Type, Is.EqualTo(TokenType.EndOfFile));
    }

    [Test]
    public void Tokenize_CreateTable_ProducesExpectedTokenStream()
    {
        var types = TypesOf("CREATE TABLE users (id INTEGER, name TEXT)");

        Assert.That(types, Is.EqualTo(new[]
        {
            TokenType.Create, TokenType.Table, TokenType.Identifier, TokenType.LeftParen,
            TokenType.Identifier, TokenType.Integer, TokenType.Comma,
            TokenType.Identifier, TokenType.Text, TokenType.RightParen,
            TokenType.EndOfFile,
        }));
    }

    [Test]
    public void Tokenize_Keywords_AreCaseInsensitive()
    {
        Assert.That(TypesOf("select")[0], Is.EqualTo(TokenType.Select));
        Assert.That(TypesOf("Select")[0], Is.EqualTo(TokenType.Select));
        Assert.That(TypesOf("SELECT")[0], Is.EqualTo(TokenType.Select));
    }

    [Test]
    public void Tokenize_Identifier_PreservesOriginalCase()
    {
        var token = Tokenize("MyTable")[0];

        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Text, Is.EqualTo("MyTable"));
    }

    [Test]
    public void Tokenize_Identifier_AllowsLeadingUnderscoreAndDigits()
    {
        var token = Tokenize("_col1")[0];

        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Text, Is.EqualTo("_col1"));
    }

    [Test]
    public void Tokenize_Number_ReadsDigitsAsNumberLiteral()
    {
        var token = Tokenize("01234")[0];

        Assert.That(token.Type, Is.EqualTo(TokenType.NumberLiteral));
        Assert.That(token.Text, Is.EqualTo("01234"));
    }

    [Test]
    public void Tokenize_String_StripsQuotesAndKeepsContent()
    {
        var token = Tokenize("'hello world'")[0];

        Assert.That(token.Type, Is.EqualTo(TokenType.StringLiteral));
        Assert.That(token.Text, Is.EqualTo("hello world"));
    }

    [Test]
    public void Tokenize_String_UnescapesDoubledQuotes()
    {
        var token = Tokenize("'it''s fine'")[0];

        Assert.That(token.Text, Is.EqualTo("it's fine"));
    }

    [Test]
    public void Tokenize_DoubleQuoted_IsAnIdentifierNotAString()
    {
        var token = Tokenize("\"users\"")[0];

        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Text, Is.EqualTo("users"));
    }

    [Test]
    public void Tokenize_DoubleQuoted_MayHoldAKeywordOrSpaces()
    {
        // Quoting is exactly how you name a table "select" or "My Table".
        Assert.That(Tokenize("\"select\"")[0].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(Tokenize("\"My Table\"")[0].Text, Is.EqualTo("My Table"));
    }

    [Test]
    public void Tokenize_DoubleQuoted_UnescapesDoubledQuotes()
    {
        var token = Tokenize("\"a\"\"b\"")[0];

        Assert.That(token.Text, Is.EqualTo("a\"b"));
    }

    [Test]
    public void Tokenize_UnterminatedQuotedIdentifier_Throws()
    {
        Assert.That(() => Tokenize("\"oops"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Tokenize_Position_PointsAtStartOfToken()
    {
        // "name" begins at index 7 in "SELECT name".
        var token = Tokenize("SELECT name")[1];

        Assert.That(token.Position, Is.EqualTo(7));
    }

    [TestCase("=", TokenType.Equal)]
    [TestCase("<>", TokenType.NotEqual)]
    [TestCase("!=", TokenType.NotEqual)]
    [TestCase("<", TokenType.Less)]
    [TestCase("<=", TokenType.LessEqual)]
    [TestCase(">", TokenType.Greater)]
    [TestCase(">=", TokenType.GreaterEqual)]
    [TestCase("*", TokenType.Asterisk)]
    [TestCase(",", TokenType.Comma)]
    [TestCase("(", TokenType.LeftParen)]
    [TestCase(")", TokenType.RightParen)]
    [TestCase(";", TokenType.Semicolon)]
    public void Tokenize_OperatorsAndPunctuation_MapToTheRightType(string source, TokenType expected)
    {
        Assert.That(Tokenize(source)[0].Type, Is.EqualTo(expected));
    }

    [Test]
    public void Tokenize_UnterminatedString_Throws()
    {
        Assert.That(() => Tokenize("'oops"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Tokenize_LoneBang_Throws()
    {
        Assert.That(() => Tokenize("a ! b"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Tokenize_UnexpectedCharacter_Throws()
    {
        Assert.That(() => Tokenize("a @ b"), Throws.TypeOf<SqlSyntaxException>());
    }
}
