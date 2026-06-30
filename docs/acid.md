# ACID — what it actually means mechanically

ACID is four guarantees a database makes about transactions. The textbook
one-liners are easy; the *mechanisms* are the interesting part.

A **transaction** is a group of reads/writes treated as a single unit:
`BEGIN … COMMIT` (or `ROLLBACK`).

## A — Atomicity: all-or-nothing

Every write in a transaction either *all* takes effect, or *none* does. If the
process crashes mid-transaction, recovery must leave no half-applied changes.

**How it's achieved:**
- **Write-ahead log (WAL):** record the *intent* to change before changing data.
  On restart you can *redo* committed transactions and *undo* uncommitted ones.
- **Copy-on-write / shadow paging:** write new copies, flip a pointer atomically.

**OSQL's approach:** an **append-only command log**. We append each statement of a
transaction, and only a `COMMIT` record makes it "real." On replay, a transaction
without a matching commit is ignored → atomicity.

## C — Consistency: valid state → valid state

A transaction moves the database from one *valid* state to another, where "valid"
means all declared rules hold (types, `NOT NULL`, unique keys, foreign keys…).

Note: consistency is *shared* responsibility. The DB enforces declared constraints;
the application is responsible for semantic correctness (e.g. "debits == credits").

**OSQL's approach:** minimal — enforce column types (`INTEGER`/`TEXT`) and arity.
We can add `NOT NULL` / `UNIQUE` later.

## I — Isolation: concurrent transactions don't step on each other

Running transactions concurrently should yield a result *as if* they ran in some
serial order. Weaker **isolation levels** trade correctness for concurrency:

| Level | Prevents… | Allows… |
|---|---|---|
| Read Uncommitted | (almost nothing) | dirty reads |
| Read Committed | dirty reads | non-repeatable reads |
| Repeatable Read | non-repeatable reads | phantoms |
| Serializable | everything | (least concurrency) |

**Two families of mechanism:**
- **Locking (2-Phase Locking):** acquire locks, release only at commit.
- **MVCC (multi-version):** keep row versions so readers never block writers (Postgres).

**OSQL's approach:** a **single global writer lock**. Only one transaction mutates at
a time → trivially **serializable**. The simplest *honest* isolation. We trade
concurrency for clarity; that's a fine deal for a learning DB.

## D — Durability: committed means committed

Once `COMMIT` returns success, the data survives a crash — even if it was only in
memory a millisecond earlier.

**The magic primitive is `fsync`:** writing to a file usually lands in an OS buffer,
*not* the physical disk. `fsync` forces the buffer to durable storage. The rule:

> **fsync the log _before_ acknowledging the commit.**

**OSQL's approach:** append the commit record to the log, `fsync`, *then* reply OK.

## How the four interact in OSQL

```
BEGIN ──► append ops to log (not yet durable/visible)
          │
COMMIT ──► append COMMIT record ──► fsync ──► release writer lock ──► reply OK
          │                          │
       Atomicity                  Durability
   (no COMMIT = ignored)      (survives crash)

Isolation = single writer lock held BEGIN→COMMIT
Consistency = type/constraint checks before each write is accepted
```

## Things to try later (rabbit holes)

- Replace the single lock with row-level 2PL, observe deadlocks.
- Add MVCC snapshots and see readers stop blocking.
- Group-commit: batch fsyncs across transactions for throughput.
