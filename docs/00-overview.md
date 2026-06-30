# OSQL — Overview & Plan

OscarSQL (OSQL) is a toy, single-node, ACID-ish SQL database built for learning.
**Not** intended for real use. The goal is to *feel* what ACID, query planning,
storage, and indexing actually mean by building each piece.

## Architecture at a glance

```
TCP plumbing ─┐
              ├─► Lexer ─► Parser (AST) ─► Planner ─► Executor ─► Storage engine
   Client ────┘                                          │            │
                                                     Catalog      Append-only log
                                                                      │
                                                                  Index (IIndex)
```

- **OSQL.Server** — listens on a TCP socket, runs the query pipeline, owns storage.
- **OSQL.Client** — a REPL that sends SQL lines and prints results.
- **OSQL.ServiceDefaults** — Aspire scaffolding; largely unused for now.

## Scope decisions (locked for the first build night)

| Concern | Decision | Why |
|---|---|---|
| Isolation (**I**) | **Single global writer lock** | Serializes all transactions → true serializable isolation for free. Simplest honest path to ACID. See [acid.md](acid.md). |
| Durability + Atomicity (**D**, **A**) | **Append-only command log + fsync**, replayed on startup | Gets durability & atomicity almost for free; intuitive intro to the WAL idea. See [storage-101.md](storage-101.md). |
| Indexing | **Abstract `IIndex` interface**, swap algorithms for fun | Don't commit to a structure yet — try SortedDictionary, B+Tree, hash. See [indexing.md](indexing.md). |
| SQL surface | `CREATE TABLE`, `INSERT`, `SELECT … WHERE`, `CREATE INDEX` | Enough to demo the whole pipeline end-to-end. See [logical-query-order.md](logical-query-order.md). |
| Types | `INTEGER`, `TEXT` only | Keep the type system trivial. |
| Queries | Single table in `FROM`, `WHERE` = `column op literal`, no JOINs | Keep planner tractable. |
| Protocol | Length-prefixed **text** over TCP | See [wire-protocol.md](wire-protocol.md). |

## Milestones (each one is demoable)

- **M0 — Plumbing:** TCP server + client REPL, length-prefixed text protocol. Echo SQL back.
- **M1 — SQL front-end:** lexer + recursive-descent parser → AST for the Core 4.
- **M2 — End-to-end in-memory:** crude planner + iterator executor (seq-scan + filter + projection). `INSERT` then `SELECT` returns rows.
- **M3 — Persistence:** survive a restart via the append-only log.
- **M4 — Transactions:** `BEGIN`/`COMMIT`/`ROLLBACK` + log → real A & D, crash-recovery on startup.
- **M5 — Indexing:** `CREATE INDEX`; planner picks index scan over seq scan when `WHERE` matches.

M0–M3 alone is a real persistent query engine. M4/M5 are stretch-but-doable.

## Docs index

- [acid.md](acid.md) — what A/C/I/D mechanically mean.
- [logical-query-order.md](logical-query-order.md) — how SQL keywords are *evaluated*, not written.
- [storage-101.md](storage-101.md) — pages, heaps, the catalog, our append-only log.
- [indexing.md](indexing.md) — indexes as sorted maps; the `IIndex` abstraction.
- [query-pipeline.md](query-pipeline.md) — lex → parse → plan → execute (Volcano model).
- [wire-protocol.md](wire-protocol.md) — our tiny TCP protocol.
