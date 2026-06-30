# Storage 101 — how bytes become a database

How do rows actually live on disk, and how do we make writes survive a crash?

## The vocabulary

- **Page** — a fixed-size block (commonly 4 KB or 8 KB), the unit of disk I/O.
  Real databases read/write whole pages, never single rows, because disks and the
  OS work in blocks. A "heap file" is just a sequence of pages.
- **Heap file** — an unordered collection of rows (a "heap"). Rows are appended
  wherever there's free space; order is not guaranteed.
- **Row / tuple** — one record. Needs a stable address so indexes can point at it:
  a **RID** (row id), often `(pageNumber, slotNumber)`.
- **Slotted page** — a page layout where the header has a slot array pointing at
  variable-length rows packed from the end. Lets rows move within a page without
  changing their slot number.
- **Catalog** — the database's metadata *about itself*: which tables exist, their
  columns/types, which indexes exist. Often stored as just another table.

## Two ways to be durable

### A. Page heap + Write-Ahead Log (the "real" layout)

Data lives in pages; before modifying a page you append a log record describing the
change. On crash you replay the log (redo committed, undo uncommitted). This is what
Postgres/SQLite/etc. do. Powerful, but a lot of moving parts (buffer pool, page
cache, checkpointing, LSNs…).

### B. Append-only command log (OSQL's choice)

Instead of mutating pages in place, we keep an **append-only log of commands**:

```
CREATE TABLE users (id INTEGER, name TEXT)
INSERT users (1, "ada")
INSERT users (2, "alan")
BEGIN
INSERT users (3, "grace")
COMMIT
```

- **Durability:** append the record, `fsync`, then ack. Once on disk, it's permanent.
- **Atomicity:** a `BEGIN…COMMIT` block only "counts" if the `COMMIT` is present on
  replay. A torn/partial tail without `COMMIT` is ignored.
- **Recovery = replay:** on startup, read the log top-to-bottom and rebuild the
  in-memory state. The current state is just "the log, folded."

This is essentially **event sourcing**, and it's the simplest honest path to A+D.

#### Trade-offs (and the natural next steps)

- The log grows forever → later add **compaction / snapshots** (periodically write
  the current state and truncate the log).
- Replay cost grows with log size → snapshots fix that too.
- Reads are served from the **in-memory state** we fold the log into, so they're fast;
  the log is only the durability mechanism, not the read path.

## OSQL's storage model (tonight)

```
┌────────────────────────────┐
│ In-memory state            │   ← reads & WHERE filters run here
│  ├─ Catalog (tables, cols) │
│  ├─ Table heaps (List<Row>)│
│  └─ Indexes (IIndex)       │
└────────────┬───────────────┘
             │ every committed write also…
             ▼
┌────────────────────────────┐
│ Append-only log file       │   ← fsync'd; replayed on startup
└────────────────────────────┘
```

Start with the simplest possible record format (one text line per command, or a
small length-prefixed binary record). We can harden the format later.
