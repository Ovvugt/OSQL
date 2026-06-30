namespace OSQL.Core.Lexing;

/// <summary>The kind of a lexical token in OSQL's SQL subset.</summary>
public enum TokenType
{
    // Identifiers and literals
    Identifier,
    NumberLiteral,
    StringLiteral,

    // Keywords
    Create,
    Table,
    Index,
    Insert,
    Into,
    Values,
    Select,
    From,
    Where,
    On,
    Not,
    Null,
    Unique,
    Serial,

    // Type keywords
    Integer,
    Text,

    // Comparison operators
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    // Punctuation
    Comma,
    LeftParen,
    RightParen,
    Asterisk,
    Semicolon,

    // End of input
    EndOfFile,
}
