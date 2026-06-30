using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace OSQL.Core.Storage;

/// <summary>
/// The append-only command log: OSQL's whole durability mechanism. Each committed
/// statement is appended as one self-describing record and forced to disk before the
/// write is acknowledged. On startup the log is read back top-to-bottom to rebuild
/// in-memory state — the database's current state is just "the log, folded."
///
/// Record layout (big-endian, mirroring <c>OSQL.Wire.WireProtocol</c>):
/// <code>
///   ┌─────────┬────────────────┬──────────────┬──────────────────────────┐
///   │ version │ payload length │ CRC-32 (IEEE)│ payload                  │
///   │ 1 byte  │ 4 bytes int32  │ 4 bytes      │ &lt;length&gt; bytes, UTF-8 SQL │
///   └─────────┴────────────────┴──────────────┴──────────────────────────┘
/// </code>
/// The CRC covers the length field and the payload, so damage to either is caught.
/// A record that was only half-written before a crash is detected and discarded; damage
/// that is not a clean tail is reported rather than silently skipped.
/// </summary>
public sealed class CommandLog : IDisposable
{
    private const int VersionOffset = 0;
    private const int LengthOffset = 1;
    private const int CrcOffset = 5;
    private const int HeaderSize = 9;
    private const int LengthFieldSize = 4;

    // Same ceiling as the wire protocol: guards against a damaged length header
    // making us try to allocate something absurd.
    private const int MaxPayloadSize = 16 * 1024 * 1024;

    private readonly FileStream _file;

    private CommandLog(FileStream file) => _file = file;

    /// <summary>Open (or create) the log file at <paramref name="path"/> for read and append.</summary>
    public static CommandLog Open(string path)
    {
        var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return new CommandLog(file);
    }

    /// <summary>
    /// Read every intact record from the start of the log, in order. A final record
    /// that was truncated or fails its checksum — the signature of a crash mid-append —
    /// is dropped and the file is trimmed back to the last good record. A checksum
    /// failure with valid data after it is genuine corruption and throws
    /// <see cref="LogCorruptionException"/>. Leaves the file positioned for appends.
    /// </summary>
    public IReadOnlyList<string> ReadAll()
    {
        _file.Seek(0, SeekOrigin.Begin);
        var records = new List<string>();
        var header = new byte[HeaderSize];
        long validEnd = 0; // offset just past the last record we fully trust

        while (true)
        {
            var headerRead = ReadFully(header);
            if (headerRead == 0)
            {
                break; // clean end of the log
            }

            if (headerRead < HeaderSize)
            {
                TruncateTo(validEnd); // torn header: crashed mid-append
                break;
            }

            var version = header[VersionOffset];
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(LengthOffset));
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(CrcOffset));

            if (length < 0 || length > MaxPayloadSize)
            {
                // The length field itself is damaged. Recoverable only if it's the tail.
                if (EndOfRecoverableTail(validEnd))
                {
                    break;
                }

                throw new LogCorruptionException(
                    $"Log record at offset {validEnd} declares an invalid length ({length}).");
            }

            var payload = new byte[length];
            if (ReadFully(payload) < length)
            {
                TruncateTo(validEnd); // torn payload: crashed mid-append
                break;
            }

            if (version != StorageFormat.Version || !ChecksumMatches(header, payload, storedCrc))
            {
                // A fully-framed record that does not verify. A torn tail can look like
                // this; anything written after it cannot — so position tells us which.
                if (EndOfRecoverableTail(validEnd))
                {
                    break;
                }

                throw new LogCorruptionException(
                    $"Log record at offset {validEnd} failed its version/checksum check.");
            }

            records.Add(Encoding.UTF8.GetString(payload));
            validEnd = _file.Position;
        }

        _file.Seek(0, SeekOrigin.End);
        return records;
    }

    /// <summary>
    /// Append one record and force it to durable storage before returning. The
    /// <c>fsync</c> here is the durability point: until it completes the bytes may sit
    /// in an OS buffer, so the caller must not acknowledge the write until this returns.
    /// </summary>
    public void Append(string statement)
    {
        var payload = Encoding.UTF8.GetBytes(statement);
        if (payload.Length > MaxPayloadSize)
        {
            throw new OsqlException(
                $"Statement of {payload.Length} bytes exceeds the {MaxPayloadSize}-byte log record limit.");
        }

        var header = new byte[HeaderSize];
        header[VersionOffset] = StorageFormat.Version;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(LengthOffset), payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(CrcOffset), Checksum(header, payload));

        _file.Seek(0, SeekOrigin.End);
        _file.Write(header);
        _file.Write(payload);
        _file.Flush(flushToDisk: true); // fsync — not durable until this returns
    }

    // The CRC is taken over the length field and the payload (not the version byte,
    // which is checked for equality, nor the CRC field itself).
    private static uint Checksum(byte[] header, byte[] payload)
    {
        var crc = new Crc32();
        crc.Append(header.AsSpan(LengthOffset, LengthFieldSize));
        crc.Append(payload);
        return crc.GetCurrentHashAsUInt32();
    }

    private static bool ChecksumMatches(byte[] header, byte[] payload, uint storedCrc) =>
        Checksum(header, payload) == storedCrc;

    // A bad record is a recoverable tail only when nothing follows it. If so, trim the
    // file back to the last good record and report that the tail was dropped.
    private bool EndOfRecoverableTail(long validEnd)
    {
        if (_file.Position < _file.Length)
        {
            return false; // there is more data after the bad record — real corruption
        }

        TruncateTo(validEnd);
        return true;
    }

    private void TruncateTo(long length)
    {
        _file.SetLength(length);
        _file.Seek(length, SeekOrigin.Begin);
    }

    // Read until the buffer is full or the stream ends; returns the count actually read.
    private int ReadFully(Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = _file.Read(buffer[read..]);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }

    public void Dispose() => _file.Dispose();
}
