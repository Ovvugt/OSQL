namespace OSQL.Core.Storage;

/// <summary>
/// Constants for the on-disk paging layer. A page is the fixed-size unit of disk I/O:
/// the heap files are sequences of pages, and the buffer pool caches whole pages, never
/// individual rows — exactly as a real engine does, because disks and the OS work in blocks.
/// </summary>
public static class Paging
{
    /// <summary>Bytes per page. 8 KB to match PostgreSQL.</summary>
    public const int PageSize = 8192;
}
