namespace OSQL.Core.Storage;

/// <summary>
/// A row's stable on-disk address: which page holds it, and which slot within that page.
/// <c>PageNumber * Paging.PageSize</c> is the byte offset of the page in the heap file.
/// This is what an index will later point at — PostgreSQL calls the same thing a <c>ctid</c>.
/// </summary>
public readonly record struct Rid(int PageNumber, int SlotNumber)
{
    public override string ToString() => $"({PageNumber}, {SlotNumber})";
}
