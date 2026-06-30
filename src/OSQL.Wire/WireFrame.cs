using System.Text;

namespace OSQL.Wire;

/// <summary>
/// One decoded message off the wire: the protocol version it arrived with, and
/// its raw payload bytes. The payload is opaque at this layer; higher layers
/// decide what the bytes mean (for now, UTF-8 text like "ping").
/// </summary>
public readonly record struct WireFrame(byte Version, byte[] Payload)
{
    /// <summary>Interpret the payload as UTF-8 text.</summary>
    public string AsText() => Encoding.UTF8.GetString(Payload);

    /// <summary>Build a frame carrying UTF-8 text, stamped with the current version.</summary>
    public static WireFrame FromText(string text) =>
        new(WireProtocol.Version, Encoding.UTF8.GetBytes(text));
}
