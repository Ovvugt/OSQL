# Wire protocol — talking to OSQL over TCP

The client and server speak over a bare TCP socket. We need a tiny protocol so both
sides agree where one message ends and the next begins.

## The framing problem

TCP is a **byte stream**, not a message stream. If the client sends `SELECT 1` and
then `SELECT 2`, the server might read `SELECT 1SELE` then `CT 2` — TCP doesn't
preserve message boundaries. So we must **frame** messages ourselves.

Two common ways:
- **Delimiter-based** — messages end with a sentinel (e.g. `\n`). Simple, but breaks
  if the payload contains the delimiter.
- **Length-prefixed** — send the byte length first, then exactly that many bytes.
  Robust for arbitrary payloads. **This is what OSQL uses.**

## OSQL frame format

```
┌──────────────┬───────────────────────────┐
│ length (4 B) │ UTF-8 payload (length B)   │
│ int32, BE    │                            │
└──────────────┴───────────────────────────┘
```

- 4-byte big-endian `int32` length header, then that many UTF-8 bytes.
- Both requests and responses use the same frame.

## Message bodies (text, for now)

Keep it human-readable while we build; we can switch to binary later.

**Request** — just the SQL text:

```
INSERT INTO users (id, name) VALUES (1, 'ada');
```

**Response** — a one-line status, then optional result rows. A simple shape:

```
OK 1                          ← rows affected (for writes)
```

```
ROWS 2                        ← result set (for SELECT)
id,name
1,ada
2,alan
```

```
ERR syntax error near 'FRMO' ← error
```

We can formalize this into a small enum of response kinds (`Ok`, `Rows`, `Err`) once
the executor exists.

## Connection lifecycle (M0)

```
Client                         Server
  │  connect ───────────────────►│ accept
  │  frame("SELECT 1") ─────────►│ read frame → lex/parse/plan/execute
  │◄──────────── frame("ROWS …") │ write framed response
  │  (loop)                      │
  │  close ─────────────────────►│ cleanup
```

For tonight: one connection handled at a time is fine (the single-writer lock means
concurrency wouldn't buy us much yet — see [acid.md](acid.md)). We can move to a
connection-per-task loop later.

## Why not just use HTTP / gRPC / a driver?

Because the *point* is to feel the raw mechanics: framing, byte streams, and the
request/response loop that every database driver hides from you.
