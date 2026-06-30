using System.Buffers.Binary;

namespace OSQL.Core.Storage;

/// <summary>
/// Durable counters for a table's SERIAL columns. Each value is persisted before it is
/// used, so a value is never reused across a restart. The write is atomic — a temp file is
/// fsync'd and then renamed over the real one — so a crash can never leave a torn counter.
/// Like a real sequence, a crash can still leave a <i>gap</i> (a value is reserved, then the
/// row's insert is lost); closing that gap needs the transactional WAL. One small file per
/// table, <c>&lt;tableId&gt;.seq</c>, holding (columnIndex, lastValue) pairs.
/// </summary>
public sealed class SequenceFile
{
    private const int EntrySize = sizeof(int) + sizeof(long);

    private readonly string _path;
    private readonly Dictionary<int, long> _last; // serial column index -> last value handed out

    private SequenceFile(string path, Dictionary<int, long> last)
    {
        _path = path;
        _last = last;
    }

    /// <summary>Open the counters for the given serial columns, loading any persisted values.</summary>
    public static SequenceFile Open(string path, IEnumerable<int> serialColumns)
    {
        var last = new Dictionary<int, long>();
        foreach (var column in serialColumns)
        {
            last[column] = 0;
        }

        if (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            for (var offset = 0; offset + EntrySize <= bytes.Length; offset += EntrySize)
            {
                var column = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
                var value = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset + sizeof(int)));
                last[column] = value;
            }
        }

        return new SequenceFile(path, last);
    }

    /// <summary>Hand out the next value for a column, persisting it before returning.</summary>
    public long Next(int columnIndex)
    {
        var value = _last[columnIndex] + 1;
        _last[columnIndex] = value;
        Persist();
        return value;
    }

    /// <summary>Advance the counter past an explicitly supplied value so it is never re-handed-out.</summary>
    public void Observe(int columnIndex, long value)
    {
        if (value > _last[columnIndex])
        {
            _last[columnIndex] = value;
            Persist();
        }
    }

    private void Persist()
    {
        var buffer = new byte[_last.Count * EntrySize];
        var offset = 0;
        foreach (var (column, value) in _last)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), column);
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset + sizeof(int)), value);
            offset += EntrySize;
        }

        var temp = _path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(buffer);
            file.Flush(flushToDisk: true); // fsync the new contents before they replace the old
        }

        File.Move(temp, _path, overwrite: true); // atomic replace: never a torn .seq file
    }
}
