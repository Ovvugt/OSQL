namespace OSQL.Core;

/// <summary>
/// Base type for errors OSQL deliberately reports back to the client: a malformed
/// statement, a broken schema rule, a type mismatch. The server turns any of these
/// into an <c>ERROR:</c> reply. It is distinct from infrastructure failures (a torn
/// log, a bad data directory), which are not routine and abort startup instead.
/// </summary>
public class OsqlException(string message) : Exception(message);
