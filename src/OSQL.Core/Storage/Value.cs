using OSQL.Core.Ast;

namespace OSQL.Core.Storage;

/// <summary>
/// One cell value: an <see cref="DataType.Integer"/> (a <see cref="long"/>), a
/// <see cref="DataType.Text"/> (a <see cref="string"/>), or SQL <c>NULL</c>. The tagged
/// shape keeps the type explicit at runtime, which is where type-checking and (later)
/// constraint enforcement naturally live. Construct values through the factories;
/// <see cref="Type"/> is <c>null</c> precisely when the value is NULL.
/// </summary>
public readonly record struct Value
{
    private readonly long _integer;
    private readonly string? _text;

    private Value(DataType? type, long integer, string? text)
    {
        Type = type;
        _integer = integer;
        _text = text;
    }

    /// <summary>The value's type, or <c>null</c> for SQL NULL.</summary>
    public DataType? Type { get; }

    /// <summary>SQL NULL.</summary>
    public static readonly Value Null = new(null, 0, null);

    /// <summary>An INTEGER value.</summary>
    public static Value Integer(long value) => new(DataType.Integer, value, null);

    /// <summary>A TEXT value.</summary>
    public static Value Text(string value) =>
        new(DataType.Text, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public bool IsNull => Type is null;

    /// <summary>The integer payload; throws if this value is not an INTEGER.</summary>
    public long AsInteger => Type == DataType.Integer
        ? _integer
        : throw new OsqlException($"Value is {Describe()}, not an INTEGER.");

    /// <summary>The text payload; throws if this value is not TEXT.</summary>
    public string AsText => Type == DataType.Text
        ? _text!
        : throw new OsqlException($"Value is {Describe()}, not TEXT.");

    private string Describe() => Type switch
    {
        DataType.Integer => "an INTEGER",
        DataType.Text => "TEXT",
        _ => "NULL",
    };

    public override string ToString() => Type switch
    {
        DataType.Integer => _integer.ToString(),
        DataType.Text => $"'{_text}'",
        _ => "NULL",
    };
}
