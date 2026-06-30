using OSQL.Core;
using OSQL.Core.Ast;
using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class RowFormatTests
{
    private static RowFormat Format(params ColumnDefinition[] columns) =>
        new(new TableSchema("t", columns));

    private static ColumnDefinition Int(string name) => new(name, DataType.Integer);
    private static ColumnDefinition Txt(string name) => new(name, DataType.Text);

    [Test]
    public void RowSize_AccountsForNullBitmapAndColumns()
    {
        // null bitmap ceil(2/8)=1, INTEGER 8, TEXT 2+255=257  ->  266
        Assert.That(Format(Int("id"), Txt("name")).RowSize, Is.EqualTo(266));
    }

    [Test]
    public void EncodeThenDecode_RoundTripsValues()
    {
        var format = Format(Int("id"), Txt("name"));

        var decoded = format.Decode(format.Encode(new[] { Value.Integer(42), Value.Text("ada") }));

        Assert.That(decoded[0], Is.EqualTo(Value.Integer(42)));
        Assert.That(decoded[1], Is.EqualTo(Value.Text("ada")));
    }

    [Test]
    public void EncodeThenDecode_PreservesNulls()
    {
        var format = Format(Int("id"), Txt("name"));

        var decoded = format.Decode(format.Encode(new[] { Value.Integer(1), Value.Null }));

        Assert.That(decoded[0], Is.EqualTo(Value.Integer(1)));
        Assert.That(decoded[1].IsNull, Is.True);
    }

    [Test]
    public void EncodeThenDecode_HandlesEmptyAndMaxLengthText()
    {
        var format = Format(Txt("a"), Txt("b"));
        var atMax = new string('x', RowFormat.TextMaxBytes);

        var decoded = format.Decode(format.Encode(new[] { Value.Text(""), Value.Text(atMax) }));

        Assert.That(decoded[0].AsText, Is.EqualTo(""));
        Assert.That(decoded[1].AsText, Is.EqualTo(atMax));
    }

    [Test]
    public void Encode_WrongValueCount_Throws()
    {
        var format = Format(Int("id"), Txt("name"));

        Assert.That(() => format.Encode(new[] { Value.Integer(1) }), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Encode_TypeMismatch_Throws()
    {
        var format = Format(Int("id"));

        Assert.That(() => format.Encode(new[] { Value.Text("nope") }), Throws.TypeOf<OsqlException>());
    }

    [Test]
    public void Encode_OversizedText_Throws()
    {
        var format = Format(Txt("a"));
        var tooBig = new string('x', RowFormat.TextMaxBytes + 1);

        Assert.That(() => format.Encode(new[] { Value.Text(tooBig) }), Throws.TypeOf<OsqlException>());
    }
}
