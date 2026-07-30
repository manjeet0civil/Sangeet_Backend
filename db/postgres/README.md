# SQL Server ➜ PostgreSQL (Supabase) migration

The API now talks to **PostgreSQL** instead of SQL Server. This folder holds the ported schema and
the 32 stored procedures, rewritten as PL/pgSQL functions.

| File | What it is |
|------|-----------|
| `01_schema.sql` | 6 tables, keys, constraints, partial unique indexes, RLS |
| `02_functions.sql` | the 32 stored procedures, as PL/pgSQL functions |
| `../../tools/DbMigrator` | console app: applies both scripts, then copies every row across |

---

## Run the migration

```powershell
# from the repo root
dotnet run --project tools/DbMigrator -- --pg "<your Supabase connection string>"
```

It prints what it's doing and finishes with a row-count comparison of both databases. Everything
runs in one transaction — if any row fails, PostgreSQL is left untouched.

Options: `--schema-only`, `--data-only`, `--truncate` (empty the target first),
`--sql "<conn>"` (a different SQL Server source). The connection string can also come from the
`SUPABASE_DB_URL` environment variable instead of the command line.

Re-running is safe: rows are upserted by primary key, so a second run refreshes rather than
duplicates.

### ⚠️ Which Supabase connection string?

`db.<ref>.supabase.co` has **an IPv6 address only**. On an IPv4-only network (most home/office
connections, including this one) it fails with *"no such host"*. Use the **Session pooler** string:

> Supabase dashboard ➜ Project Settings ➜ Database ➜ Connection string ➜ **Session pooler**

```
postgresql://postgres.<project-ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres
```

Note the username is `postgres.<project-ref>`, not plain `postgres`. Port 5432 is session mode
(what this app wants); 6543 is transaction mode, which does not support every feature.

---

## How the port was done

### Types

| SQL Server | PostgreSQL | Why |
|---|---|---|
| `uniqueidentifier` | `uuid` | direct equivalent |
| `nvarchar(n)` | `varchar(n)` | PostgreSQL text is Unicode already |
| `nvarchar(MAX)` | `text` | |
| `bit` | `boolean` | |
| `datetime2` | `timestamp` (no time zone) | the app has always stored UTC, as `GETUTCDATE()` did |
| `GETUTCDATE()` | `timezone('utc', now())` | |

### Case sensitivity — the subtle one

SQL Server's default collation is **case-insensitive**, so `'A@b.com' = 'a@b.com'` was true; that
governed the unique constraint on `Email`, the duplicate checks on `UserName` and `PlaylistName`,
and `LIKE` in song search. PostgreSQL is case-**sensitive** by default, which would silently allow
two accounts differing only in capitalisation.

Preserved by:
- `email`, `username`, `playlistname` use the **`citext`** type (case-insensitive text);
- song search uses **`ILIKE`** instead of `LIKE`;
- role checks compare `lower(role)` and store the canonical `'User'` / `'Admin'`.

### Errors

SQL Server raised `THROW 50001, 'Email already exists.', 1` and `SqlErrorTranslator` mapped the
number to an HTTP status. PostgreSQL has no equivalent, so each function raises the **same number
as a custom SQLSTATE**:

```sql
RAISE EXCEPTION 'Email already exists.' USING ERRCODE = '50001';
```

`PostgresErrorTranslator.cs` parses `SqlState` back to an int and applies the original
code→status table, so the API returns exactly the same responses (409/404/400) as before.

### Calling the functions

Dapper used `CommandType.StoredProcedure`. Npgsql translates that to `CALL`, which only works for
PostgreSQL *procedures*, not the *functions* used here. So `RepositoryBase` builds the call itself
using **named-argument notation**:

```sql
SELECT * FROM procaccountinsert(p_accountid => @AccountId, p_email => @Email, ...)
```

The argument list is generated from the properties of the anonymous parameter object, so:

> **every function parameter must be named `p_` + the lower-cased C# property name.**
> Rename one without the other and the call fails with *"function does not exist"*.

Because the notation is named, argument order doesn't matter.

### Pagination

`procSongGetByUploader` returned **two result sets** (a page, then the total). A PostgreSQL function
returns one, so the count lives in `procsonggetbyuploader_total`, which takes the same arguments.
`RepositoryBase.QueryPageAsync` sends both statements in a single command and reads them with
Dapper's `QueryMultiple` — still one round trip.

### Transactions

The T-SQL procedures wrapped writes in `BEGIN TRAN … COMMIT` with `TRY/CATCH … ROLLBACK`. A
PL/pgSQL function already runs inside a single transaction and `RAISE EXCEPTION` rolls the whole
thing back, so those blocks were simply dropped — same guarantees, less code.

### Identifier case

PostgreSQL folds unquoted identifiers to lower case, so tables and columns are `songid`,
`durationinseconds`, and so on. Dapper matches result columns to C# properties **case-insensitively**,
so `SongId` and `DurationInSeconds` still bind — no C# entity changes were needed.

---

## Security note — Row Level Security

Supabase exposes every table in the `public` schema through PostgREST, reachable by anyone holding
the project's **anon key**. Left open, `account.passwordhash` would be readable that way.

`01_schema.sql` therefore runs `ALTER TABLE … ENABLE ROW LEVEL SECURITY` on all six tables and
creates **no policies**, which denies that path entirely. The API is unaffected: it connects as the
table owner over the direct Postgres connection, and owners bypass RLS.

Do **not** add `FORCE ROW LEVEL SECURITY` — that applies RLS to the owner too and would lock the
application out of its own tables.

---

## Rolling back to SQL Server

Nothing was deleted from SQL Server — it still holds the original data. To go back, restore the
previous `SqlConnectionFactory`/`SqlErrorTranslator` (in git history), swap the `Dapper`
`CommandType.StoredProcedure` calls back in `RepositoryBase`, and point
`ConnectionStrings:MusicDatabase` at `MusicDatabaseSqlServerOld` from `appsettings.json`.
