using System.Buffers.Binary;

namespace OSQL.Core.Storage;

/// <summary>
/// A view over one page's bytes that lays out fixed-width rows. The 8-byte header tracks
/// how many slots are filled; rows are packed front-to-back, so slot <c>i</c> never moves
/// once written and its <see cref="Rid"/> stays stable. (Deletes — which need a free-slot
/// bitmap so freeing one slot doesn't shift the others — and the reserved page-LSN field,
/// which the write-ahead log will use, are later additions.)
///
/// <code>
///   [0..4)  page LSN   (reserved for the WAL)
///   [4..6)  rowCount
///   [6..8)  flags      (reserved)
///   [8..)   slot 0 | slot 1 | …      each rowSize bytes
/// </code>
/// </summary>
public sealed class HeapPage
{
    public const int HeaderSize = 8;

    private const int RowCountOffset = 4;

    private readonly byte[] _page;
    private readonly int _rowSize;

    /// <summary>Wrap an existing page buffer. A freshly zeroed buffer is a valid empty page.</summary>
    public HeapPage(byte[] page, int rowSize)
    {
        if (page.Length != Paging.PageSize)
        {
            throw new ArgumentException($"A page must be exactly {Paging.PageSize} bytes.", nameof(page));
        }

        _page = page;
        _rowSize = rowSize;
    }

    /// <summary>How many rows the page currently holds.</summary>
    public int RowCount => BinaryPrimitives.ReadUInt16BigEndian(_page.AsSpan(RowCountOffset));

    /// <summary>How many rows fit in a page at this row size.</summary>
    public int Capacity => (Paging.PageSize - HeaderSize) / _rowSize;

    /// <summary>Append a row if there is room, returning its slot number.</summary>
    public bool TryInsert(ReadOnlySpan<byte> row, out int slot)
    {
        var count = RowCount;
        if (count >= Capacity)
        {
            slot = -1;
            return false;
        }

        row.CopyTo(_page.AsSpan(SlotOffset(count), _rowSize));
        SetRowCount(count + 1);
        slot = count;
        return true;
    }

    /// <summary>The raw bytes of a filled slot.</summary>
    public ReadOnlySpan<byte> GetRow(int slot)
    {
        if (slot < 0 || slot >= RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "No such row in this page.");
        }

        return _page.AsSpan(SlotOffset(slot), _rowSize);
    }

    private void SetRowCount(int count) =>
        BinaryPrimitives.WriteUInt16BigEndian(_page.AsSpan(RowCountOffset), (ushort)count);

    private int SlotOffset(int slot) => HeaderSize + slot * _rowSize;
}
