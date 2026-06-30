namespace OSQL.Core.Storage;

/// <summary>
/// A table the database knows about: its <see cref="Id"/> (which names its
/// <c>&lt;id&gt;.heap</c> file), its <see cref="Schema"/>, and the open
/// <see cref="HeapFile"/> holding its rows. Ids are handed out in creation order, and
/// because the DDL log replays in that same order they come back identically on restart.
/// </summary>
public sealed class Table(int id, TableSchema schema, HeapFile heap) : IDisposable
{
    public int Id { get; } = id;
    public TableSchema Schema { get; } = schema;
    public HeapFile Heap { get; } = heap;
    public string Name => Schema.Name;

    public void Dispose() => Heap.Dispose();
}
