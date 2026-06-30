# Query pipeline — lex → parse → plan → execute

The journey of a single SQL string from text to rows. Four stages.

```
"SELECT id FROM users WHERE age > 30"
        │
        ▼  ① LEX            stream of tokens
   [SELECT][id][FROM][users][WHERE][age][>][30]
        │
        ▼  ② PARSE          AST (tree of meaning)
   SelectStmt
     ├─ select: [id]
     ├─ from:   users
     └─ where:  (age > 30)
        │
        ▼  ③ PLAN           operator tree (how to execute)
   Project(id)
     └─ Filter(age > 30)
          └─ SeqScan(users)
        │
        ▼  ④ EXECUTE        pull rows through the tree
   → { id: 7 }, { id: 9 }, …
```

## ① Lexer (tokenizer)

Turns characters into **tokens**: keywords (`SELECT`), identifiers (`users`),
literals (`30`, `"ada"`), operators (`>`, `=`), punctuation (`,`, `(`, `;`).
Skips whitespace. This is a simple character-by-character scanner.

## ② Parser → AST

Consumes tokens and builds an **Abstract Syntax Tree** — a typed object graph that
captures *meaning*, not syntax. We'll use **recursive descent**: one method per
grammar rule (`ParseSelect`, `ParseExpression`, …). Each statement type becomes a
node: `CreateTableStmt`, `InsertStmt`, `SelectStmt`, `CreateIndexStmt`.

The AST is "what the user asked for" with no decisions about *how* to do it yet.

## ③ Planner → operator tree

Decides *how* to execute. For us, crude but real:

- `FROM` + `WHERE col = v` → if an index on `col` exists, emit **IndexScan**;
  else **SeqScan** + **Filter**.
- `SELECT cols` → **Project**.

The output is a tree of operators. This is where [logical-query-order.md](logical-query-order.md)
becomes concrete: the tree *is* the evaluation order (FROM at the leaf, SELECT near
the root).

## ④ Executor — the Volcano / iterator model

Each operator is an **iterator** exposing something like `bool MoveNext()` /
`Row Current`. Execution is **pull-based**: the root asks its child for the next
row, which asks *its* child, all the way down to the scan at the leaf.

```csharp
public interface IOperator
{
    bool MoveNext();   // advance; false when exhausted
    Row Current { get; }
}
```

- **SeqScan** — yields rows from the table heap one at a time.
- **IndexScan** — yields rows whose RIDs came from `index.Seek(value)`.
- **Filter(child, predicate)** — pulls from `child`, skips rows failing the predicate.
- **Project(child, columns)** — pulls from `child`, returns only chosen columns.

Why pull-based? It's lazy (great for `LIMIT`), composable (operators don't know their
neighbors' internals), and memory-light (one row in flight at a time). This is the
"Volcano" model, named after the 1990s research system.

## Putting it together (server request handling)

```
read SQL line from socket
   → Lexer.Tokenize
   → Parser.Parse            → AST
   → Planner.Plan            → IOperator tree
   → take writer lock (single-writer isolation)
   → Executor.Run(tree)      → rows / rowcount
       (writes also append to the log, fsync on COMMIT)
   → release lock
   → write result back to socket
```
