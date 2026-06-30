# OSQL (OscarSQL)

A tiny ACID-ish SQL database, built to show how databases work under the hood.
It is a toy and is not meant for production. The point is to make the mechanics that
real databases hide visible and easy to follow: parsing SQL, planning queries,
storing data so it survives a crash, and indexing so lookups stay fast.

The server and client talk over a bare TCP socket, and everything is .NET.

## What's in here

- **OSQL.Server** listens on a socket, parses and runs SQL, and owns the data.
- **OSQL.Client** is a small REPL that sends SQL and prints the results.
- **docs/** explains what each concept means before it gets built (ACID, query
  planning, storage, indexing). Start with `docs/00-overview.md`.

## The rough plan

Small scope, working end to end:

1. TCP plumbing: client talks to server, messages get framed properly.
2. A lexer and parser for `CREATE TABLE`, `INSERT`, `SELECT ... WHERE`.
3. A crude planner and an executor that actually returns rows.
4. Persistence so data survives a restart.
5. Transactions with real durability and crash recovery.
6. Indexing, so the planner can skip full table scans.

## Scope, on purpose

- Two types: `INTEGER` and `TEXT`.
- One table per query, no joins yet.
- One writer at a time, which keeps transactions simple and correct.

See the docs for the reasoning behind each of these.
