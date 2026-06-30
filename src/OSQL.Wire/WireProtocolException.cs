namespace OSQL.Wire;

/// <summary>
/// Thrown when bytes on the wire don't match the protocol: an unknown version,
/// or a payload length outside the allowed range.
/// </summary>
public sealed class WireProtocolException(string message) : Exception(message);
