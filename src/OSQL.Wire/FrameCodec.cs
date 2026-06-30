using System.Buffers.Binary;
using System.Text;

namespace OSQL.Wire;

/// <summary>
/// Reads and writes <see cref="WireFrame"/>s on a stream according to the
/// <see cref="WireProtocol"/> framing. This is the one place that knows how a
/// message turns into bytes and back, so client and server can't disagree.
/// </summary>
public static class FrameCodec
{
    /// <summary>Write a payload as a single framed message and flush it.</summary>
    public static async Task WriteFrameAsync(
        Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        if (payload.Length > WireProtocol.MaxPayloadSize)
        {
            throw new WireProtocolException(
                $"Payload of {payload.Length} bytes exceeds the {WireProtocol.MaxPayloadSize}-byte limit.");
        }

        var header = new byte[WireProtocol.HeaderSize];
        header[WireProtocol.VersionOffset] = WireProtocol.Version;
        BinaryPrimitives.WriteInt32BigEndian(
            header.AsSpan(WireProtocol.LengthOffset), payload.Length);

        await stream.WriteAsync(header, ct);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, ct);
        }

        await stream.FlushAsync(ct);
    }

    /// <summary>Write UTF-8 text as a single framed message.</summary>
    public static Task WriteTextAsync(Stream stream, string text, CancellationToken ct = default)
    {
        return WriteFrameAsync(stream, Encoding.UTF8.GetBytes(text), ct);
    }

    /// <summary>
    /// Read the next frame. Returns <c>null</c> when the peer closed the
    /// connection cleanly at a frame boundary (i.e. there's simply nothing more
    /// to read). Throws <see cref="WireProtocolException"/> on a malformed frame
    /// and <see cref="EndOfStreamException"/> if the stream ends mid-frame.
    /// </summary>
    public static async Task<WireFrame?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[WireProtocol.HeaderSize];
        if (!await ReadFullyAsync(stream, header, ct))
        {
            return null; // clean close: no bytes waiting for us
        }

        var version = header[WireProtocol.VersionOffset];
        if (version != WireProtocol.Version)
        {
            throw new WireProtocolException(
                $"Unsupported protocol version {version}; this build speaks version {WireProtocol.Version}.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(WireProtocol.LengthOffset));
        if (length is < 0 or > WireProtocol.MaxPayloadSize)
        {
            throw new WireProtocolException($"Declared payload length {length} is out of range.");
        }

        var payload = new byte[length];
        if (length > 0 && !await ReadFullyAsync(stream, payload, ct))
        {
            throw new EndOfStreamException("Stream ended in the middle of a frame payload.");
        }

        return new WireFrame(version, payload);
    }

    /// <summary>
    /// Fill <paramref name="buffer"/> completely. Returns false only when the
    /// stream ends before a single byte is read (a clean close at a boundary);
    /// throws if it ends after a partial read (a truncated frame).
    /// </summary>
    private static async Task<bool> ReadFullyAsync(
        Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new EndOfStreamException(
                    $"Expected {buffer.Length} bytes but stream closed after {read}.");
            }

            read += n;
        }

        return true;
    }
}
