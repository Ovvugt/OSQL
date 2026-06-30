# Indexing — an index is just a sorted map from key → row

Without an index, finding `WHERE id = 42` means scanning every row (a **sequential
scan**, O(n)). An index trades extra writes + space for fast lookups.

## The core idea

An index is a **map from a column value to the location(s) of matching rows**:

```
key (indexed column)        →   RID(s) (where the row lives)
─────────────────────────────────────────────
1                           →   row#0
2                           →   row#1
42                          →   row#7
```

With that map, `WHERE id = 42` is one lookup instead of a full scan. If the map is
also **ordered**, range queries (`WHERE id BETWEEN 10 AND 20`) become a range walk.

## Structures (the part we're keeping pluggable)

| Structure | Lookup | Range scan | Notes |
|---|---|---|---|
| **Hash map** | O(1) | ❌ no order | Dictionary; equality only. |
| **Sorted array** | O(log n) read, O(n) insert | ✅ | Cache-friendly, slow writes. |
| **Balanced BST** | O(log n) | ✅ | `SortedDictionary` = red-black tree, free in .NET. |
| **B+Tree** | O(log n) | ✅ | What real disk DBs use: wide nodes = fewer page reads. |

**OSQL's plan:** don't commit. Define an `IIndex` interface and try several backings
for fun — measure and compare.

## The `IIndex` abstraction (sketch)

```csharp
public interface IIndex
{
    // Maintain the index as rows change.
    void Insert(IndexKey key, Rid rid);
    void Delete(IndexKey key, Rid rid);

    // Point lookup: rows exactly matching key.
    IEnumerable<Rid> Seek(IndexKey key);

    // Range scan (inclusive bounds); ordered structures support this,
    // hash-backed ones can throw NotSupportedException.
    IEnumerable<Rid> Range(IndexKey? low, IndexKey? high);
}
```

This lets the planner ask "is there an index on this column, and can it serve this
predicate?" without caring whether it's a hash, a tree, or a B+Tree underneath.

## Clustered vs secondary (terminology for later)

- **Clustered index** — the table rows are physically stored *in* index order
  (the index *is* the table). At most one per table.
- **Secondary index** — a separate structure pointing back at rows by RID. Many allowed.

OSQL's indexes are **secondary** (heap stays the source of truth; index points at RIDs).

## How the planner uses it

For `SELECT … FROM t WHERE col = v`:

1. Does an index exist on `col`? → if yes, **IndexScan**: `index.Seek(v)` → RIDs → rows.
2. Otherwise → **SeqScan**: walk the whole heap, apply the filter.

That single decision is our "crude query planner." See
[query-pipeline.md](query-pipeline.md).

## `CREATE INDEX` (M5)

```sql
CREATE INDEX ON users (id);
```

Parse → add to catalog → build the chosen `IIndex` by scanning existing rows →
keep it maintained on every future `INSERT`.
