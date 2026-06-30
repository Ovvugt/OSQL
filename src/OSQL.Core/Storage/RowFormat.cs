using System.Buffers.Binary;
using System.Text;
using OSQL.Core.Ast;

namespace OSQL.Core.Storage;

/// <summary>
/// Encodes and decodes one table's rows to and from their fixed-width byte layout. The
/// schema is the decoder: there are no per-row type tags. A row is a null bitmap (one bit
/// per column, set when the column is NULL) followed by each column at its fixed size.
///
/// <code>
///   row = [ null bitmap: ceil(C/8) B ][ col0 ][ col1 ] … [ colN ]
///   INTEGER → 8 bytes, big-endian
///   TEXT    → 2-byte length + TextMaxBytes buffer, padded
/// </code>
///
/// Fixed widths mean every row is <see cref="RowSize"/> bytes, so slot <c>i</c> sits at a
/// computable offset. Variable-length TEXT (slotted pages) and oversized values (overflow
/// pages) are deliberate later upgrades; for now TEXT is capped at <see cref="TextMaxBytes"/>.
/// </summary>
public sealed class RowFormat
{
    /// <summary>Maximum encoded length of a TEXT value, in bytes, for the fixed-width layout.</summary>
    public const int TextMaxBytes = 255;

    private const int IntegerSize = sizeof(long);
    private const int TextLengthPrefix = sizeof(ushort);
    private const int TextSize = TextLengthPrefix + TextMaxBytes;

    private readonly IReadOnlyList<ColumnDefinition> _columns;
    private readonly int _nullBitmapSize;
    private readonly int[] _offsets;

    public RowFormat(TableSchema schema)
    {
        _columns = schema.Columns;
        _nullBitmapSize = (_columns.Count + 7) / 8;

        _offsets = new int[_columns.Count];
        var offset = _nullBitmapSize;
        for (var i = 0; i < _columns.Count; i++)
        {
            _offsets[i] = offset;
            offset += SizeOf(_columns[i].Type);
        }

        RowSize = offset;
    }

    /// <summary>The fixed number of bytes every row of this table occupies.</summary>
    public int RowSize { get; }

    /// <summary>Encode one row. Throws on the wrong column count, a type mismatch, or oversized TEXT.</summary>
    public byte[] Encode(IReadOnlyList<Value> values)
    {
        if (values.Count != _columns.Count)
        {
            throw new OsqlException(
                $"Row has {values.Count} value(s) but the table has {_columns.Count} column(s).");
        }

        var row = new byte[RowSize];
        for (var i = 0; i < _columns.Count; i++)
        {
            var value = values[i];
            if (value.IsNull)
            {
                row[i / 8] |= (byte)(1 << (i % 8)); // set the null bit; leave the slot zeroed
                continue;
            }

            var column = _columns[i];
            if (value.Type != column.Type)
            {
                throw new OsqlException(
                    $"Column '{column.Name}' is {column.Type}, but the value is {value.Type}.");
            }

            var field = row.AsSpan(_offsets[i]);
            switch (column.Type)
            {
                case DataType.Integer:
                    BinaryPrimitives.WriteInt64BigEndian(field, value.AsInteger);
                    break;
                case DataType.Text:
                    WriteText(field, column.Name, value.AsText);
                    break;
            }
        }

        return row;
    }

    /// <summary>Decode one row's bytes back into values, using the schema to interpret them.</summary>
    public Value[] Decode(ReadOnlySpan<byte> row)
    {
        var values = new Value[_columns.Count];
        for (var i = 0; i < _columns.Count; i++)
        {
            var isNull = (row[i / 8] & (1 << (i % 8))) != 0;
            if (isNull)
            {
                values[i] = Value.Null;
                continue;
            }

            var field = row[_offsets[i]..];
            values[i] = _columns[i].Type switch
            {
                DataType.Integer => Value.Integer(BinaryPrimitives.ReadInt64BigEndian(field)),
                DataType.Text => Value.Text(ReadText(field)),
                _ => Value.Null,
            };
        }

        return values;
    }

    private static void WriteText(Span<byte> field, string columnName, string text)
    {
        var byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount > TextMaxBytes)
        {
            throw new OsqlException(
                $"TEXT value for column '{columnName}' is {byteCount} bytes, over the {TextMaxBytes}-byte limit.");
        }

        BinaryPrimitives.WriteUInt16BigEndian(field, (ushort)byteCount);
        Encoding.UTF8.GetBytes(text, field[TextLengthPrefix..]);
    }

    private static string ReadText(ReadOnlySpan<byte> field)
    {
        var byteCount = BinaryPrimitives.ReadUInt16BigEndian(field);
        return Encoding.UTF8.GetString(field.Slice(TextLengthPrefix, byteCount));
    }

    private static int SizeOf(DataType type) => type switch
    {
        DataType.Integer => IntegerSize,
        DataType.Text => TextSize,
        _ => throw new OsqlException($"Unsupported column type {type}."),
    };
}
