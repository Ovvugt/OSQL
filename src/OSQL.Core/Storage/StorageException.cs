namespace OSQL.Core.Storage;

/// <summary>
/// The data directory exists but was written under a storage format this build does
/// not understand. Crossing formats is a deliberate migration, never automatic, so
/// this aborts startup rather than risk misreading the data.
/// </summary>
public sealed class StorageFormatException(string message) : Exception(message);

/// <summary>
/// The log holds damage that is not a recoverable torn tail (a checksum failure with
/// valid records after it, or a statement that no longer applies). Silently skipping it
/// would drop committed data, so recovery refuses to continue and surfaces this instead.
/// </summary>
public sealed class LogCorruptionException(string message) : Exception(message);
