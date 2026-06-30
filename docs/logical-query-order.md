# Logical query order — how SQL is *evaluated*, not written

SQL is written in one order but *logically evaluated* in another. Understanding
this ordering is the key to the planner: the plan tree is literally this pipeline.

## Written order (what you type)

```sql
SELECT   columns
FROM     table
WHERE    condition
GROUP BY columns
HAVING   condition
ORDER BY columns
LIMIT    n
```

## Logical evaluation order (what the engine does)

```
1. FROM      → pick the source rows (which table/scan)
2. WHERE     → filter rows (row-by-row predicate)
3. GROUP BY  → bucket rows into groups
4. HAVING    → filter groups
5. SELECT    → project / compute output columns
6. ORDER BY  → sort the result
7. LIMIT     → cut to N rows
```

This is why you **can't** reference a `SELECT` alias in `WHERE` (WHERE runs first),
but you **can** in `ORDER BY` (it runs after SELECT). The ordering isn't arbitrary —
it falls out of dependencies between stages.

## What OSQL implements tonight

Only the bolded stages — enough for an end-to-end demo:

```
1. FROM    → sequential scan of one table   ✅
2. WHERE   → filter: column op literal      ✅
5. SELECT  → projection: pick columns        ✅
   (GROUP BY / HAVING / ORDER BY / LIMIT — later)
```

So a query like:

```sql
SELECT id, name FROM users WHERE age > 30;
```

becomes the operator pipeline (read bottom-up — see [query-pipeline.md](query-pipeline.md)):

```
Project(id, name)
  └─ Filter(age > 30)
       └─ SeqScan(users)
```

## Mapping keywords → AST → plan

| SQL keyword | AST node | Plan operator |
|---|---|---|
| `FROM users` | `FromClause` | `SeqScan` / `IndexScan` |
| `WHERE age > 30` | `WhereClause(BinaryExpr)` | `Filter` |
| `SELECT id, name` | `SelectList` | `Project` |

The planner's whole job (for now) is: turn the AST into this little operator tree,
and decide `SeqScan` vs `IndexScan` for the `FROM`/`WHERE` pair.
