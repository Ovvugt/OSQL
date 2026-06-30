namespace OSQL.Core;

/// <summary>Raised when input can't be lexed or parsed as valid OSQL SQL.</summary>
public sealed class SqlSyntaxException(string message) : OsqlException(message);
