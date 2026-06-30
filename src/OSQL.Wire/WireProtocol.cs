namespace OSQL.Wire;

/// <summary>
/// Constants that define the OSQL wire protocol shared by client and server.
///
/// Every message is a single frame:
///
///   ┌───────────┬──────────────────┬──────────────────────────┐
///   │ version   │ payload length   │ payload                  │
///   │ 1 byte    │ 4 bytes, int32   │ &lt;length&gt; bytes, UTF-8    │
///   │           │ big-endian       │                          │
///   └───────────┴──────────────────┴──────────────────────────┘
///
/// The 5-byte header is fixed size, so a reader always knows exactly how many
/// bytes to pull before it can size the payload. The version byte lets us change
/// the framing later without guessing what an old client is speaking.
/// </summary>
public static class WireProtocol
{
    /// <summary>Current protocol version. Bump this when the framing changes.</summary>
    public const byte Version = 1;

    /// <summary>Size of the fixed header: 1 version byte + 4 length bytes.</summary>
    public const int HeaderSize = 5;

    /// <summary>Byte offset of the version field within the header.</summary>
    public const int VersionOffset = 0;

    /// <summary>Byte offset of the payload-length field within the header.</summary>
    public const int LengthOffset = 1;

    /// <summary>
    /// Upper bound on a single payload (16 MiB). Guards against a malformed or
    /// hostile length header making us allocate something absurd.
    /// </summary>
    public const int MaxPayloadSize = 16 * 1024 * 1024;

    /// <summary>Default TCP port the server listens on.</summary>
    public const int DefaultPort = 5470;
}
