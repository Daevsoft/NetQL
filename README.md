# NetQL

**A lightweight, fluent SQL query builder over ADO.NET.** Build parameterized `SELECT`, `INSERT`, `UPDATE`, and `DELETE` statements, map rows to POCOs, or process a reader incrementally. Both synchronous and cancellation-aware asynchronous execution are available.

Author: Muhamad Deva Arofi · [GitHub](https://github.com/daevsoft) · [Apache 2.0](LICENSE.txt)

This README describes the **current repository source**, not an assumed published package version. The two libraries share synchronized `Lib` implementations:

| Project | Entry point | Target framework |
|---|---|---|
| `netQL/NetQL.csproj` | `Daevsoft.NetQL` | `net5.0` |
| `NetQL.Core/NetQL.Core.csproj` | `Daevsoft.Core.NetQL` | `netcoreapp3.1` |

Both targets are **end-of-life**. The changes documented here do not upgrade them. Plan and validate a supported-framework migration separately. The examples use a .NET 8 consumer; that does not change the library targets. Reference **one** library, not both: their `Daevsoft.Lib` types overlap.

NetQL is not an ORM with change tracking, lazy loading, relationship fix-up, migrations, or compile-time-checked column expressions. Table names, column names, operators, and raw SQL remain application-controlled strings.

## Contents

- [Installation and imports](#installation-and-imports)
- [Runnable SQLite quick start](#runnable-sqlite-quick-start)
- [Choosing an API](#choosing-an-api)
- [Sessions, ownership, and statement state](#sessions-ownership-and-statement-state)
- [Providers and portability](#providers-and-portability)
- [Security and parameterized raw SQL](#security-and-parameterized-raw-sql)
- [Reading and composing queries](#reading-and-composing-queries)
- [Writing data](#writing-data)
- [Transactions and atomicity](#transactions-and-atomicity)
- [Async, cancellation, and timeouts](#async-cancellation-and-timeouts)
- [Application recipes](#application-recipes)
- [Mapping and reader behavior](#mapping-and-reader-behavior)
- [Migration notes](#migration-notes)
- [Testing and troubleshooting](#testing-and-troubleshooting)
- [Limitations and deployment checklist](#limitations-and-deployment-checklist)

## Installation and imports

Use a project reference; no NuGet package ID for NetQL is assumed. From a separate development folder, create the quick-start consumer:

```bash
dotnet new console --framework net8.0 --name NetQLDemo
dotnet add NetQLDemo/NetQLDemo.csproj reference "path/to/NetQL/netQL/NetQL.csproj"
dotnet add NetQLDemo/NetQLDemo.csproj package Microsoft.Data.Sqlite --version 8.0.22
```

Replace the reference path with the actual repository location. For Core, reference `NetQL.Core/NetQL.Core.csproj` **instead**. A compatible SDK/targeting pack and package-feed access or a populated cache are needed to build the legacy targets.

Install an ADO.NET driver in the **consumer application**. Neither library references a database driver package. Select driver versions compatible with the consumer runtime and deployment database; the SQLite version above matches the isolated regression project.

Common imports for the fragments below:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Daevsoft;
using Daevsoft.Lib;
using Order = Daevsoft.Lib.QueryCommon.Order;
```

For Core, replace `using Daevsoft;` with `using Daevsoft.Core;`. `Provider`, `Str`, `DbUtils`, and `ReaderUtil` stay in `Daevsoft.Lib`. `Order` is nested in `QueryCommon`; use the alias above or `.Asc(...)` / `.Desc(...)` if your application has an `Order` entity.

Except for the complete quick-start and Web API programs, code blocks are fragments or methods to place in your application. They assume the common imports, an appropriately configured `db`, an existing schema, and any DTOs shown or named. NetQL does not provision those tables.

## Runnable SQLite quick start

Replace the console application's `Program.cs` with this complete program. It creates a disposable, in-memory SQLite database: **no business database or credentials are involved**.

```csharp
using System;
using System.ComponentModel.DataAnnotations.Schema;
using Daevsoft;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

await using var db = new NetQL(
    new SqliteConnection("Data Source=:memory:"), Provider.SQLite);

// An in-memory SQLite database disappears when its connection closes.
// Keep this owned connection open until await using disposes it.
db.IsKeepAlive(true);
db.CommandTimeout(30);

await db.Query(@"
    CREATE TABLE hotels (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        room INTEGER NOT NULL,
        city TEXT NULL
    )").ExecuteAsync();

int inserted = await db.Insert("hotels").Bulk(new[]
{
    new { id = 1, name = "Developer Hotel", room = 120, city = "London" },
    new { id = 2, name = "Garden Hotel", room = 80, city = "Paris" }
}).ExecuteAsync();

var query = db.Select("id, name, room, city", "hotels")
    .Where("room", ">=", 100)
    .Wrap(g => g.Where("city", "London").OrWhere("city", "Paris"))
    .Asc("name").Asc("id")
    .Limit(10);

// Repeated previews do not open a connection, execute SQL, or add parameters.
string sql = query.GenerateQuery();
if (sql != query.GenerateQuery())
    throw new InvalidOperationException("Unexpected SQL preview change.");

var hotels = await query.ReadAsListAsync<Hotel>();
Console.WriteLine($"Inserted {inserted} rows.");
foreach (var hotel in hotels)
    Console.WriteLine($"{hotel.Id}: {hotel.Name} ({hotel.City})");

public class Hotel
{
    public int Id { get; set; }
    [Column("name")]
    public string Name { get; set; } = "";
    public int Room { get; set; }
    public string? City { get; set; }
}
```

Run:

```bash
dotnet run --project NetQLDemo/NetQLDemo.csproj
```

Expected output:

```text
Inserted 2 rows.
1: Developer Hotel (London)
```

There is no implicit NetQL transaction in this example; the database handles each command in its normal autocommit mode. Disposal destroys the in-memory database. For a file/server database, writes persist unless you explicitly roll them back. SQLite's ADO.NET async methods may execute synchronously; an `await` here does not promise asynchronous disk I/O.

## Choosing an API

| Goal | Synchronous API | Asynchronous API / result |
|---|---|---|
| Build a selection | `Select(table)` or `Select(columns, table)` | Offline construction; columns come **before** table |
| Map every row | `ReadAsList<T>()` → `List<T>` | `ReadAsListAsync<T>(token)` → `Task<List<T>>` |
| Map the first row | `ReadAs<T>()` → `T` | `ReadAsAsync<T>(token)` → `Task<T>` |
| Read first column of first row | `ReadSingle<T>()` → `T` | `ReadSingleAsync<T>(token)` → `Task<T>` |
| Read a flat column | `ReadAsArray<T>()` → `IEnumerable<T>` | No matching async array helper; use `ReadAsync` |
| Process rows incrementally | `Read(callback)` → `int` | `ReadAsync(callback, token)` → `Task<int>` |
| Test whether any row exists | `IsExist()` → `bool` | `IsExistAsync(token)` → `Task<bool>` |
| Execute a nonquery and get affected rows | `ExecuteNonQuery()` → `int` | `ExecuteAsync(token)` → `Task<int>` |
| Retain legacy callback execution | `Execute(Action<dynamic>)` → active transaction or `null` | Prefer `ExecuteAsync` and inspect its count |
| Complete an owned transaction | `Commit()` / `Rollback()` | `CommitAsync(token)` / `RollbackAsync(token)` → `Task` |
| Release the session | `Dispose()` | `DisposeAsync()` → `ValueTask`; use `await using` |
| Preview a read or write | `GenerateQuery()` → SQL text | Pure preview; no async version needed |

No rows means an empty list/array, zero callback visits, `false` for existence, or `default(T)` for first-row/scalar reads (`null` for a class or nullable value). First-row methods do not assert uniqueness. `ReadAsArray<T>()` materializes its results; its `IEnumerable<T>` return type is **not** a streaming contract.

`Insert`, `Update`, and `Delete` return a `SqlModel` with `Execute`, `ExecuteNonQuery`, `ExecuteAsync`, and `GenerateQuery`. Counts are provider-reported affected rows, not generated IDs; DDL, triggers, and matched-versus-changed-row conventions can affect them.

## Sessions, ownership, and statement state

### Constructors

Both entry points expose the same constructor choices:

| Constructor | Use |
|---|---|
| `NetQL(IDbConnection)` | Infer provider from the connection's runtime type name |
| `NetQL(IDbConnection, Provider)` | Select the SQL dialect explicitly; recommended for wrappers/alternative drivers |
| `NetQL(IDbConnection, Provider, bool leaveOpen)` | Explicitly choose connection ownership |
| `NetQL(string connectionString, Provider)` | Instantiate an installed driver through reflection |
| `NetQL(IDbConnection, char quotSql, char bindSymbol = '@')` | Legacy custom quoting; provider inference still matters for paging |

```csharp
// NetQL owns this connection, including its eventual disposal.
using var owned = new NetQL(new SqliteConnection(connectionString), Provider.SQLite);

// The caller owns closing/disposal of this connection.
using var connection = new SqliteConnection(connectionString);
using var borrowed = new NetQL(connection, Provider.SQLite, leaveOpen: true);
```

The second form may open the connection on first execution, but leaves it open afterwards for the owner. Prefer passing a connection when configuring driver-specific settings or avoiding reflection.

Auto-detection recognizes `SqlConnection`, `MySqlConnection`, `NpgsqlConnection`, `OracleConnection`, and `SqliteConnection`. It is name-based, not a general provider-discovery mechanism. For proxies or `System.Data.SQLite.SQLiteConnection`, use `Provider` explicitly.

### Ownership and lifetime

| Mode | Between commands | On disposal |
|---|---|---|
| Default owned connection, no transaction | Open on execution; close after each command | Dispose connection |
| Owned connection with `IsKeepAlive(true)` | Keep open | Still dispose connection |
| Owned transaction | Keep connection/transaction for sequential commands | Roll back unfinished owned transaction; dispose owned connection |
| `leaveOpen: true` | Do not automatically close connection | Leave connection to caller; still finish any NetQL-owned transaction |
| Attached caller transaction | Borrow transaction **and** connection | Do not commit, roll back, or dispose caller transaction/connection |
| Query clone | Borrow parent connection for construction only | Does not close/dispose parent connection |

`IsKeepAlive(bool)` returns `void`, so call it separately. It controls connection lifetime, **not** transaction atomicity. Driver connection pooling is a separate facility; keeping every session open indefinitely is not a pooling strategy.

`Close()` clears pending statement state, rolls back an unfinished **owned** transaction, and closes an owned connection unless keep-alive is enabled. It is not a commit operation. The legacy `Close(true)` explicitly forces connection closure, even for a borrowed connection; **do not use it on `leaveOpen` or external-transaction sessions**. Normal `using` / `await using` is preferable to forced closure.

Attaching an external transaction transfers connection-lifetime responsibility to its caller. After the caller completes that transaction, dispose the wrapper and create a fresh one rather than executing with a completed attachment.

### Offline construction and sequential reuse

- `Query()` and `Select()` do not open the connection or begin a transaction. `Query()` replaces the previous base SQL, clauses, and pending parameters; `Select()` starts a new query through the same path.
- `GenerateQuery()` on either a read or write builder is repeatable, performs no database I/O, and does not mutate the parameter collection. Custom-bind delegates should themselves be pure.
- Execution uses the **fully generated SQL**, including filters, joins, groups, ordering, paging, and subqueries. Reads and writes enlist in the active transaction.
- After an operation completes or fails, readers/commands are disposed and statement state is cleared, including grouping and parameters. Connection/transaction ownership, keep-alive, and command timeout are session settings, not per-query clauses.
- Start the next sequential operation with `Query`, `Select`, or a new write builder. Two variables pointing to one `DbUtils` are not two independent queries. Do not interleave unfinished reads and writes.
- A `SqlModel` write is **single-use**. Create a new builder for each execution, including a deliberate retry after failure. Repeated SQL previews do not consume it.
- `Clone()` creates an empty, borrowed **query-only** builder for nested composition, not an executable session or a snapshot of the parent query. Build/return it from `Wrap` or subquery callbacks; never execute it independently.

```csharp
var query = db.Select("hotels").Where("city", "London");
string firstPreview = query.GenerateQuery();
string secondPreview = query.GenerateQuery(); // same SQL; no new parameters
var london = query.ReadAsList<Hotel>();      // executes that composed query

// Fresh statement, no inherited city filter or grouping.
var paris = db.Select("hotels").Where("city", "Paris").ReadAsList<Hotel>();

// Query replaces pending parameters too: always add them after Query.
long count = db.Query("SELECT COUNT(*) FROM hotels WHERE city = @city")
    .AddParameter("city", "London", DbType.String)
    .ReadSingle<long>();
```

### Concurrency contract

**One instance per sequential unit of work. Not a singleton. Not thread-safe for parallel query construction.** An operation gate rejects overlapping execution and session mutation while a read/async operation is running; it does not turn mutable builders into a concurrent collection or queue.

Await each operation before starting the next, disposing, or completing its transaction. Do not mutate a write builder while it executes. Do not use `Task.WhenAll` with one session, run a nested command on it from a reader callback, or use clones to evade this rule. Independent concurrent work needs separate sessions/connections. A scoped DI registration still requires sequential use within that scope.

## Providers and portability

These are implemented SQL-generation choices, **not live integration certification**:

| Provider | Identifier quotes | Generated bind prefix | Paging | Multi-row `Bulk` insert | Native `BulkCopy` |
|---|---|---|---|---|---|
| `Provider.SqlServer` | `[name]` | `@` | `OFFSET … FETCH NEXT …` | Multi-row `VALUES` | `Microsoft.Data.SqlClient` / `System.Data.SqlClient` `SqlBulkCopy` |
| `Provider.MySql` | backticks | `@` | `LIMIT … OFFSET …` | Multi-row `VALUES` | `MySqlConnector.MySqlBulkCopy`; `MySql.Data` falls back to chunked SQL |
| `Provider.PostgreSQL` | `"name"` | `:` | `LIMIT … OFFSET …` | Multi-row `VALUES` | `COPY … FROM STDIN (FORMAT BINARY)` |
| `Provider.Oracle` | `"name"` | `:` | `OFFSET … FETCH NEXT …` | `INSERT ALL … SELECT 1 FROM DUAL` | `OracleBulkCopy` (no transaction enlistment, no async) |
| `Provider.SQLite` | `"name"` | `@` | `LIMIT … OFFSET …` | Multi-row `VALUES` | Falls back to chunked SQL |

- SQL Server paging requires **SQL Server 2012+** and an explicit builder `.OrderBy(...)`, `.Asc(...)`, or `.Desc(...)`. NetQL rejects `.Limit(...)` without one. An `ORDER BY` hidden in the base raw SQL does not satisfy this builder check: either use builder ordering/paging or write the **entire** paging statement yourself.
- Oracle row limiting requires **Oracle 12c+**. Oracle permits no ordering, but use deterministic ordering with a unique tie-breaker on every provider.
- Oracle commands enable `BindByName` when the driver exposes that property. Table-alias SQL omits Oracle's unsupported table `AS` form.
- `RIGHT JOIN` availability depends on server/version, notably older SQLite releases. NetQL does not emulate unsupported joins.
- Raw expressions/functions are not translated. `NOW()`, `DATE_TRUNC`, casts, JSON operators, date arithmetic, and timezone rules are database-specific.
- Booleans, `Guid`, dates, decimals, parameter sizes, collations, `LIKE`, affected-row counts, and transaction isolation must be tested against the selected driver/server.
- Bulk statements and `IN` lists consume parameters. SQL Server batches are rejected before execution when they exceed 1000 rows or 2100 parameters; `Bulk(rows, chunkSize)` splits them automatically instead, but other servers, statement length, and driver limits still require caller batching or `BulkCopy`.
- Oracle `IN`/`NOT IN` lists longer than 1000 expressions are rendered as parenthesized `OR`/`AND` chunk groups automatically (ORA-01795). Other providers keep one list; their parameter ceilings still apply.

The connection-string constructor searches these driver types, in order where alternatives are listed:

| Provider | Reflection candidates |
|---|---|
| SQL Server | `Microsoft.Data.SqlClient.SqlConnection`, then `System.Data.SqlClient.SqlConnection` |
| MySQL | `MySqlConnector.MySqlConnection`, then `MySql.Data.MySqlClient.MySqlConnection` |
| PostgreSQL | `Npgsql.NpgsqlConnection` |
| Oracle | `Oracle.ManagedDataAccess.Client.OracleConnection` |
| SQLite | `Microsoft.Data.Sqlite.SqliteConnection`, then `System.Data.SQLite.SQLiteConnection` |

The corresponding assembly must be installed/loadable by the host. Changing `Provider` neither changes the connected database nor translates arbitrary SQL. Custom quoting is not a substitute for selecting a known dialect; paging with an unknown provider is rejected.

## Security and parameterized raw SQL

### Values are data; SQL structure is trusted code

Ordinary string **values**, including strings beginning with `!!`, are always literal bound data in `Where`, membership lists, `AddValue`, `SetValue`, and object/bulk writes. Null values are handled as database nulls. Do not manually escape apostrophes or embed quotes around values.

| Input position | Rule |
|---|---|
| Ordinary values / `AddParameter` | Bind data; never concatenate it into SQL |
| Table/column/alias names | Select from an application allowlist |
| Operators and sort direction | Select constants or validated enums, not arbitrary request strings |
| `Query`, `WhereRaw`, raw join conditions | Developer-owned SQL only; bind any variable values |
| `AddRawValue` / `SetRawValue` | Explicit trusted SQL expressions, not user data |
| `Str.Raw` in identifier/expression positions | Bypasses quoting; never use request text |
| Custom-bind callback | Developer-owned transformation around a generated placeholder |

Identifier quoting is not a security boundary or a full SQL parser. Function-like identifier strings can also pass through as expressions. Parameters cannot represent table names, keywords, or sort directions. Choose tenant/security filters from authenticated application context and keep them outside optional OR groups.

```csharp
string sortColumn = requestedSort switch
{
    "name" => "name",
    "city" => "city",
    _ => "id"
};
var hotels = db.Select("hotels")
    .Where("city", suppliedCity)
    .OrderBy(sortColumn, descending ? Order.DESC : Order.ASC)
    .Asc("id")
    .Limit(100)
    .ReadAsList<Hotel>();
```

`Str.Raw("COUNT(*)")` is useful in a **selection expression**. It returns a marker-prefixed string, not a typed SQL-value object. Passing it to `.AddValue("created_at", Str.Raw("NOW()"))` now stores literal `!!NOW()`; it does **not** call a function. Use `.AddRawValue("created_at", "CURRENT_TIMESTAMP")` or `.SetRawValue(...)` for explicit raw values, after checking the dialect.

### Complete parameterized SQL

This SQLite/SQL Server-style example uses `@` placeholders; use the selected driver's accepted syntax (the builder generates `:` for PostgreSQL/Oracle).

```csharp
int changed = db.Query("UPDATE hotels SET name = @name WHERE id = @id")
    .AddParameter("name", suppliedName, DbType.String)
    .AddParameter("id", hotelId, DbType.Int32)
    .ExecuteNonQuery();

var hotel = db.Query("SELECT id, name, room, city FROM hotels WHERE id = @id")
    .AddParameter("id", hotelId, DbType.Int32)
    .ReadAs<Hotel>();
```

`AddParameter` defaults to `DbType.String`; specify types for numbers, dates, binary values, and typed nulls. C# `null` becomes `DBNull.Value`. Parameter names may be supplied without the prefix; duplicate names are rejected. Generated bind suffixes are internal: do not guess them, hard-code them, or assert exact counter values in application code.

A raw expression with **bound inputs**, rather than interpolated inputs:

```csharp
int changed = db.Update("media_files")
    .SetRawValue("file_dir", "REPLACE(file_dir, @src, @dst)")
    .Where("id", fileId)
    .AddParameter("src", sourceDirectory, DbType.String)
    .AddParameter("dst", targetDirectory, DbType.String)
    .ExecuteNonQuery();
```

Verify `REPLACE` and parameter syntax for your database. Whole raw predicates work on both read and write builders:

```csharp
var hotel = db.Select("hotels")
    .WhereRaw("room >= @minimum AND city = @city")
    .AddParameter("minimum", 100, DbType.Int32)
    .AddParameter("city", "London", DbType.String)
    .ReadAs<Hotel>();
```

For stored procedures, where supported by the driver:

```csharp
var hotels = db.Query("dbo.GetHotelsByCity", CommandType.StoredProcedure)
    .AddParameter("city", "London", DbType.String)
    .ReadAsList<Hotel>();
```

This is not SQLite SQL. Stored procedure conventions, output parameters, generated identities, and multiple result sets may require direct ADO.NET instead of these convenience APIs.

Keep credentials outside source control, use least-privilege accounts, and redact SQL/parameter logs. Parameterization does not provide authorization or escape SQL `LIKE` wildcard semantics (`%` and `_`).

## Reading and composing queries

### Selection and terminal reads

```csharp
List<Hotel> all = db.Select("hotels").ReadAsList<Hotel>();

Hotel first = db.Select("hotels").Where("id", 1).ReadAs<Hotel>();
// first can be null when there is no row.

var projected = db.Select(new[] { "id", "name", "city" }, "hotels")
    .Where("city", "London")
    .ReadAsList<Hotel>();

long count = db.Select(Str.Raw("COUNT(*)"), "hotels")
    .Where("room", ">=", 100)
    .ReadSingle<long>();

IEnumerable<string> names = db.Select("name", "hotels")
    .Asc("id")
    .ReadAsArray<string>(); // materialized, not lazy

int visited = db.Select("id, name", "hotels").Read(reader =>
{
    int id = reader.GetValue<int>("id");
    string name = reader.GetValue<string>("name");
    Console.WriteLine($"{id}: {name}");
});
```

`IsExist()` / `IsExistAsync()` execute the composed selection and stop after the first row; there is no derived-table wrapper. Select a small projection for existence checks. `SELECT COUNT(*)` always produces an aggregate row, so use a scalar count rather than `IsExist()` on that query.

```csharp
bool exists = await db.Select("id", "hotels")
    .Where("city", "London")
    .IsExistAsync(cancellationToken);
```

### Predicates, nulls, membership, and groups

```csharp
var hotels = db.Select("hotels")
    .Where("room", ">=", 50)
    .Where(new { city = "London" })
    .Wrap(g => g.Where("name", "LIKE", "%Garden%")
                .OrWhere("name", "LIKE", "%River%"))
    .ReadAsList<Hotel>();
```

Ordinary `Where` calls combine with AND; `OrWhere` combines with OR. SQL precedence still applies. `Wrap` produces an AND-connected parenthesized group; `OrWrap` produces an OR-connected group. Both are available on **both library variants**, and on read and write builders. Groups can nest. An anonymous-object `Where` adds one AND predicate per property; null properties become `IS NULL`. Use nonempty groups and simple filter objects.

```csharp
var active = db.Select("hotels")
    .Where("tenant_id", authenticatedTenantId)
    .AndNull("deleted_at")
    .Wrap(g => g.Where("city", "London").OrWhere("city", "Paris"))
    .ReadAsList<Hotel>();
```

The last fragment assumes tenant/soft-delete columns in your application schema. Keeping the OR conditions inside the group prevents them from bypassing the tenant predicate.

Null predicates:

```csharp
var missingCity = db.Select("hotels").AndNull("city").ReadAsList<Hotel>();
var knownCity = db.Select("hotels")
    .WhereRaw("city", "IS NOT", "NULL")
    .ReadAsList<Hotel>();

// Cast null to object to disambiguate overloads from subquery callbacks.
var alsoKnownCity = db.Select("hotels")
    .Where("city", "!=", (object)null)
    .ReadAsList<Hotel>();
```

Normal null equality/inequality comparisons generate `IS NULL` / `IS NOT NULL` rather than binding `= NULL`. `AndNull` / `OrNull` are the clearest explicit helpers. A custom SQL expression is not automatically rewritten into arbitrary null-aware logic; other operators still follow SQL semantics.

Membership uses arrays (convert a list with `.ToArray()`); every element is bound:

```csharp
var chosen = db.Select("hotels")
    .WhereIn("id", new[] { 1, 2, 3 })
    .WhereNotIn("city", new[] { "Paris", "Tokyo" })
    .ReadAsList<Hotel>();
```

`OrWhereIn` and `OrWhereNotIn` are also available. Empty arrays have explicit boolean meaning on reads and writes:

| Predicate | Empty array |
|---|---|
| `WhereIn` | AND false (`1 = 0`) |
| `WhereNotIn` | AND true (`1 = 1`) |
| `OrWhereIn` | OR false |
| `OrWhereNotIn` | OR true |

Oracle renders lists above 1000 expressions as `(... IN (...) OR ... IN (...))` (or the `AND` form for `NOT IN`) so the statement stays within the provider limit; other providers receive a single list. Parameter ceilings still apply to very large sets.

In particular, OR with empty NOT IN can make a group true; do not confuse that with “ignore this optional filter.” Decide at the application boundary whether an omitted filter should be omitted or given set semantics. Pass an empty array rather than a null collection: read builders reject null arrays, while the legacy write list path treats null as empty. SQL `NOT IN` containing a null element has three-valued logic; add explicit null conditions when appropriate.

### Trusted expressions and custom binds

```csharp
var matches = db.Select("hotels")
    .Where(Str.Raw("LOWER(name)"), "ALICE", bind => "LOWER(" + bind + ")")
    .ReadAsList<Hotel>();
```

The value is still bound. Only the application-owned `LOWER(...)` expression and placeholder transformation are raw. This is not a portable function registry.

`WhereRaw("predicate")` appends a whole predicate; `WhereRaw("column", ">=", "CURRENT_TIMESTAMP")` quotes the column but emits the right-hand SQL verbatim. Use `Str.Raw` on the **column** when that side is also an expression. Never manually quote untrusted values into either form.

### Joins and aliases

```csharp
var bookings = db.Select(
        new[] { "b.id", "h.name hotel_name", "c.name city_name" }, "bookings b")
    .Join("hotels h", "b.hotel_id", "h.id")
    .LeftJoin("cities c", "h.city_id", "c.id")
    .Where("b.status", "CONFIRMED")
    .Asc("b.id")
    .ReadAsList<BookingView>();

public class BookingView
{
    public long Id { get; set; }
    [Column("hotel_name")]
    public string HotelName { get; set; } = "";
    [Column("city_name")]
    public string? CityName { get; set; }
}
```

`.RightJoin(table, leftColumn, rightColumn)` is available when the server supports it. A two-argument join uses the second argument as a trusted raw ON condition:

```csharp
var query = db.Select("h.id, h.name", "headers h")
    .Join("details d", "d.header_id = h.id AND d.revision_no = h.revision_no");
```

Qualified identifiers, wildcards, and conventional aliases are quoted by the builder. Alias duplicate result column names explicitly; mapping by name chooses the first duplicate, not a relationship.

### Subqueries

Use callback builders for membership, scalar comparisons, and joins; their parameters propagate to the parent command.

```csharp
var hotels = db.Select("hotels")
    .WhereIn("city_id", sub => sub.Select("id", "cities")
        .Where("country_code", "GB"))
    .ReadAsList<Hotel>();

var belowAverage = db.Select("hotels")
    .Where("room", "<", sub => sub.Select(Str.Raw("AVG(room)"), "hotels")
        .Where("city", "London"))
    .ReadAsList<Hotel>();

var query = db.Select("h.id, h.name", "hotels h")
    .LeftJoin(sub => sub.Select("id", "cities")
            .Where("country_code", "GB").Alias("c"),
        "h.city_id", "c.id");
string sql = query.GenerateQuery();
```

For explicit join aliases, an overload accepts `Join(subquery, alias, leftColumn, rightColumn)`; left/right variants are available too. Raw `Query(...).AddParameter(...)` subqueries also propagate their parameters. Keep explicit names unique across the complete statement. Do not keep or mutate callback builders after composing the parent, and do not execute them.

Write predicates accept the same subquery style:

```csharp
int changed = db.Update("subscriptions")
    .SetValue("reminder_pending", true)
    .WhereIn("user_id", sub => sub.Select("id", "users")
        .Where("status", "ACTIVE"))
    .Where("expires_at", "<=", reminderCutoffUtc)
    .Where("reminder_pending", false)
    .ExecuteNonQuery();
```

Scheduling a reminder flag is not proof that an email was delivered. For reliable external notifications use a transactional outbox and idempotent delivery, not a “mark sent, then send” assumption.

### Aggregation, grouping, and ordering

```csharp
var totals = db.Select(
        new[] { "city", Str.Raw("COUNT(*) AS booking_count"),
                Str.Raw("SUM(total_amount) AS revenue") }, "bookings")
    .Where("status", "CONFIRMED")
    .Where("booking_date", ">=", fromUtc)
    .GroupBy("city")
    .Desc("revenue").Asc("city")
    .Limit(10)
    .ReadAsList<CityRevenue>();

public class CityRevenue
{
    public string? City { get; set; }
    [Column("booking_count")]
    public long BookingCount { get; set; }
    public decimal? Revenue { get; set; }
}
```

`.GroupBy(new List<string> { "city", "country_code" })` supplies several grouping columns. `.OrderBy("name")`, `.OrderBy("name", Order.DESC)`, `.Asc("name")`, and `.Desc("name")` are equivalent ordering forms. Repeated ordering calls append terms to one ORDER BY clause.

For a PostgreSQL-only monthly grouping, for example, use `Str.Raw("DATE_TRUNC('month', created_at)")` in both the selection and `GroupBy`. It is not translated for SQLite or SQL Server. Use complete parameterized SQL for `HAVING`, CTEs, window-query layouts, or unions that have no dedicated fluent API.

### Paging and clause order

```csharp
var page = db.Select("hotels")
    .Where("city", "London")
    .Asc("name").Asc("id")
    .Limit(start: 20, length: 10)
    .ReadAsList<Hotel>();
```

`Limit(10)` takes ten rows. `Limit(20, 10)` skips twenty and takes ten. Lengths `<= 0` and negative offsets throw rather than silently removing the limit. Validate and cap page sizes at the application boundary; compute offsets with `checked((page - 1) * pageSize)`.

Generated SQL is assembled in clause order: selection/FROM → JOIN → WHERE → GROUP BY → ORDER BY → LIMIT/OFFSET or OFFSET/FETCH, regardless of fluent call order. SQL Server requires builder ordering even if `.Limit` was called first. Oracle permits unordered paging, but it is not deterministic.

A unique tie-breaker such as `name, id` prevents arbitrary ordering of equal names. Offset paging still shifts under concurrent inserts/deletes; use [keyset pagination](#keyset-pagination-and-optional-filters) for deep sequential traversal. Separate total-count and page queries can observe different snapshots unless an appropriate transaction/isolation strategy is used.

## Writing data

### Explicit values and object writes

```csharp
int inserted = db.Insert("hotels")
    .AddValue("id", 10)
    .AddValue("name", "O'Brien's Hotel")
    .AddValue("room", (short)50, DbType.Int16)
    .AddValue("city", null, DbType.String)
    .ExecuteNonQuery();

int updated = db.Update("hotels")
    .SetValue("name", "Garden Hotel")
    .SetValue("room", 75)
    .Where("id", 10)
    .ExecuteNonQuery();

int cleared = db.Update("hotels")
    .SetValue<string>("city", null)
    .Where("id", 10)
    .ExecuteNonQuery();
```

Bare `SetValue("city", null)` cannot infer a generic type; use `SetValue<string>` or `SetValue("city", null, DbType.String)`. SQL null values are bound normally; `SetRawValue("city", "NULL")` is also an explicit SQL expression.

```csharp
int inserted = db.Insert("hotels")
    .Bulk(new { id = 11, name = "River Hotel", room = 120, city = "London" })
    .ExecuteNonQuery();

int updated = db.Update("hotels")
    .Bulk(new { name = "River Hotel Annex", room = 130 })
    .Where("id", 11)
    .ExecuteNonQuery();
```

`Update(...).Bulk(obj)` assigns columns from **one object**; it is not a batch-update API. Use a narrow write DTO or explicit assignments: NetQL does not track changed fields or automatically omit identity/computed keys.

`Insert(table, obj)` is also available and uses the same row mapper as `.Bulk(object)`, so `[Column]`, `[NotMapped]`, and underscore-prefixed properties behave identically. Collections go through the bulk path. See [mapping rules](#mapping-and-reader-behavior).

### Explicit raw values and previews

```csharp
var write = db.Insert("audit_log")
    .AddValue("action", "HOTEL_CREATED")
    .AddValue("record_id", hotelId)
    .AddRawValue("occurred_at", "CURRENT_TIMESTAMP");

Console.WriteLine(write.GenerateQuery());
Console.WriteLine(write.GenerateQuery()); // still pure; no duplicate parameters
int rows = write.ExecuteNonQuery();       // execute this write builder only once
```

Use `SetRawValue("version", "version + 1")` for a trusted update expression. `CURRENT_TIMESTAMP`, identifier case, timestamps, and arithmetic must match your schema/dialect; no raw expression is universally translated.

### Delete and affected-row guards

```csharp
int deleted = db.Delete("hotels")
    .Where("id", hotelId)
    .Where("city", expectedCity)
    .ExecuteNonQuery();
```

**UPDATE and DELETE do not require a WHERE clause.** An unfiltered write affects the whole table. Require validated keys/tenant filters in application methods, review generated SQL, and check affected counts. A count check **after an autocommit write cannot undo it**. Use an explicit transaction when a failed guard must roll back the write:

```csharp
public static void DeleteHotel(NetQL db, int id, string expectedName)
{
    if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
    db.Transaction();
    try
    {
        int deleted = db.Delete("hotels")
            .Where("id", id)
            .Where("name", expectedName)
            .ExecuteNonQuery();
        if (deleted != 1)
            throw new DBConcurrencyException("Hotel was missing or changed.");
        db.Commit();
    }
    catch (Exception error)
    {
        try { db.Rollback(); }
        catch (Exception cleanup) { error.Data["RollbackError"] = cleanup; }
        throw;
    }
}
```

This helper owns the transaction boundary; do not call it inside another component's active transaction. A real optimistic-concurrency token/version is preferable to comparing a display name. Test your provider's affected-row semantics, particularly unchanged updates.

### Bulk insert and chunking

`Bulk(rows)` snapshots and validates mapped rows, then creates **one SQL statement**:

- Empty input, null/malformed rows, duplicate mapped columns, and mismatched column sets are rejected before writing.
- Rows with the same mapped columns in a different property order are reordered to match the first row.
- Do not combine `.Bulk(...)` with `.AddValue(...)` / `.AddRawValue(...)` on one insert.
- Null and ordinary string values, including `!!` prefixes, stay bound values.
- Multiple `.Bulk(...)` calls can append compatible rows, but the resulting statement still has limits.
- SQL Server statements above 1000 rows or 2100 parameters throw before any command runs; use `Bulk(rows, chunkSize)` or `BulkCopy` instead of splitting by hand.
- There is no identity retrieval or universal upsert API.

Pass a chunk size to split the snapshot into several statements executed as **one atomic unit**:

```csharp
public static async Task<int> InsertHotelsAsync(
    NetQL db, IEnumerable<Hotel> source, CancellationToken cancellationToken)
{
    var rows = source.Select(h => new
    {
        id = h.Id, name = h.Name, room = h.Room, city = h.City
    }).ToArray();

    // 500 rows per statement; one owned transaction covers every chunk.
    return await db.Insert("hotels").Bulk(rows, chunkSize: 500)
        .ExecuteAsync(cancellationToken);
}
```

Chunking semantics:

- The returned count is the sum of **affected rows across all chunks**. The `Execute(callback)` overload reports the same total.
- Without a configured transaction, `Bulk(rows, chunkSize)` opens an owned transaction and commits once at the end. Any chunk failure rolls back every chunk.
- If the session already has `Transaction()` active, chunks join it and it stays open; you commit or roll back. Nothing is committed implicitly.
- `Bulk(rows, chunkSize, commitEachChunk: true)` opts out of atomicity: each chunk commits independently, so earlier chunks survive a later failure. This is for resumable checkpoints, not all-or-nothing writes. It is rejected if a transaction is configured.
- SQL Server chunk sizes are capped automatically at 1000 rows and at the row count that keeps parameters under 2100 (minus any additional parameters). Other providers use the requested size.
- Chunking applies to inserts only; `Update(...).Bulk(...)` is a single-statement path.
- Large transactions increase lock/log pressure; use native bulk copy for high-volume loads.

### Native bulk copy

`BulkCopy` and `BulkCopyAsync` stream POCO rows through the driver's native loader rather than generating SQL:

```csharp
public static async Task<long> UploadPricesAsync(
    NetQL db, IReadOnlyList<PriceRow> rows, CancellationToken cancellationToken)
{
    // 5,000-row batches, no NetQL transaction (provider default commit behavior).
    return await db.BulkCopyAsync("prices", rows, batchSize: 5_000,
        transactional: false, cancellationToken: cancellationToken);
}
```

- `tableName` is quoted with the provider's identifier quoting and is **not** validated against the schema; never pass user input.
- Rows may be a single object or any non-string `IEnumerable`; every row must be the same CLR type. Column names come from `[Column]` (or the property name); `[NotMapped]` and underscore-prefixed names are skipped, as in SQL writes. Getters are compiled once per type.
- `batchSize` is the number of rows materialized into each `DataTable` batch for the table-based loaders (SQL Server, MySQL, Oracle) and is also passed to `SqlBulkCopy.BatchSize`. PostgreSQL streams every row through a single `COPY` without batching. A value of `0` or less means 5000.
- `transactional: true` opens an owned transaction when none is configured, commits on success, and rolls back on failure. With `Transaction()` already active, rows join that transaction and you own the commit. The default `false` leaves commit behavior to the driver, which is already atomic per batch on most servers.
- The return value is the number of rows submitted (the provider loaders generally do not report a separate affected count).

Provider behavior and caveats:

| Provider | Implementation | Notes |
|---|---|---|
| SQL Server | `SqlBulkCopy` from the connection's assembly | Destination/`ColumnMappings` by name; streams a `DataTable` per batch |
| PostgreSQL | `COPY … FROM STDIN (FORMAT BINARY)` | Values are written with typed `NpgsqlBinaryImporter` methods; enum/CLR types map to standard PostgreSQL types |
| MySQL | `MySqlConnector.MySqlBulkCopy` | Requires `AllowLoadLocalInfile=true` in the connection string; `MySql.Data` connections fall back to chunked SQL |
| Oracle | `OracleBulkCopy` | Direct-path load: cannot enlist in any transaction, so `transactional: true` or an active `Transaction()` throws; no true async path |
| SQLite | None | Falls back to the chunked SQL path with the same `transactional` semantics |

Native loaders bypass ordinary constraints/triggers in provider-specific ways (Oracle direct path in particular). Identity columns, defaults, and computed columns must be excluded from the row type. The adapters are reflective: the driver assembly must be loadable and its expected members present. Only the SQLite fallback is exercised by this repository's live tests; verify other providers against your exact driver versions.

To plug in a different loader, implement `IBulkCopyAdapter` and call `db.UseBulkCopyAdapter(adapter)` (restore with `UseDefaultBulkCopyAdapter()`). The adapter receives a `BulkCopyContext` with the table name, resolved `BulkCopyColumn` list, batch size, timeout, connection, and current transaction.

## Transactions and atomicity

### Owned transactions

There is **no implicit transaction** by default. `Transaction()` or `Transaction(System.Data.IsolationLevel)` configures an owned transaction; connection opening and transaction begin are deferred until the first execution. Building or previewing a query does not begin it. Both reads and writes use it.

```csharp
public static async Task RenameHotelWithAuditAsync(
    NetQL db, int id, string expectedName, string newName,
    CancellationToken cancellationToken)
{
    if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
    if (string.IsNullOrWhiteSpace(newName))
        throw new ArgumentException("A name is required.", nameof(newName));

    db.Transaction(); // or Transaction(System.Data.IsolationLevel.Serializable)
    try
    {
        int changed = await db.Update("hotels")
            .SetValue("name", newName)
            .Where("id", id)
            .Where("name", expectedName)
            .ExecuteAsync(cancellationToken);
        if (changed != 1)
            throw new DBConcurrencyException("Hotel was missing or changed.");

        await db.Insert("audit_log")
            .AddValue("action", "HOTEL_RENAMED")
            .AddValue("record_id", id)
            .AddValue("occurred_at", DateTime.UtcNow, DbType.DateTime)
            .ExecuteAsync(cancellationToken);

        await db.CommitAsync(cancellationToken);
    }
    catch (Exception error)
    {
        // The failing command may already have rolled back the owned transaction.
        // Application-level guard failures still need rollback. Cleanup must not
        // inherit an already-canceled request token or hide the original error.
        try { await db.RollbackAsync(CancellationToken.None); }
        catch (Exception cleanup) { error.Data["RollbackError"] = cleanup; }
        throw;
    }
}
```

This assumes `audit_log(action, record_id, occurred_at)` with any other required columns defaulted by the schema. The rename and audit insert commit together; no audit row means no committed rename if execution fails before successful commit. The helper expects no already-configured transaction.

| Action | Owned transaction behavior |
|---|---|
| `Transaction()` | Enable deferred begin, using provider-default isolation |
| `Transaction(isolationLevel)` | Enable deferred begin with requested isolation; provider must support it |
| Successful command | Keep transaction active for the next sequential command |
| Command/reader/callback failure or cancellation | Attempt rollback of the owned transaction; preserve the original operation error |
| `Commit()` / `CommitAsync()` | Complete/dispose owned transaction and reset transaction mode |
| `Rollback()` / `RollbackAsync()` | Roll back/dispose owned transaction and reset transaction mode |
| Completion with no active transaction | Clear the pending mode; no database transaction to complete |
| Disposal / `Close()` with unfinished owned transaction | Roll back rather than commit |

Rollback after an operation already rolled back is safe; start a **new statement** before subsequent work. Completion closes an owned connection unless keep-alive is enabled. `Commit(false)` is a legacy conditional no-op in transaction mode; use the default `Commit()` for an actual completion. Changing transaction mode while a transaction is active is rejected. Nested transactions/savepoints are not a fluent feature.

Atomicity only covers commands enlisted in that database transaction. Email, HTTP calls, file writes, and another independent connection are not automatically included. A network failure or cancellation during commit can leave the outcome **unknown** to the application; reconcile by a durable business/idempotency key rather than assuming nothing committed.

### Caller-owned transactions

Attach a transaction from the **same already-open connection**. NetQL borrows it and the connection. The owner must complete it directly; NetQL `Commit` / `Rollback` (including async completion) refuse to complete an external transaction. Failure handling never automatically rolls it back.

```csharp
using var connection = new SqliteConnection(connectionString);
connection.Open();
using var transaction = connection.BeginTransaction();
using var db = new NetQL(connection, Provider.SQLite, leaveOpen: true);
db.Transaction(transaction);

try
{
    db.Update("hotels").SetValue("name", "Renamed")
        .Where("id", hotelId).ExecuteNonQuery();

    // Other commands using this same connection must also enlist in transaction.
    transaction.Commit(); // owner, not db.Commit()
}
catch
{
    transaction.Rollback(); // owner decides; NetQL does not do this for you
    throw;
}
```

The owner must also handle rollback/commit cleanup failures and ambiguous commit outcomes. On an execution failure, an external transaction may be unusable depending on the database; the owner decides rollback/recovery. Never let two components independently own transaction completion. Use `DbConnection` / `DbTransaction` implementations for async work.

## Async, cancellation, and timeouts

NetQL calls the provider's `DbConnection`, `DbCommand`, `DbDataReader`, and `DbTransaction` async APIs. It does **not** wrap synchronous queries in `Task.Run`. Async execution requires a `DbConnection` whose commands are `DbCommand`; a provider exposing only `IDbConnection`/`IDbCommand` is suitable only for synchronous execution. Attached async transactions must be `DbTransaction` instances.

Actual asynchronous I/O, cancellation responsiveness, and transaction support are driver-dependent. In particular, SQLite ADO.NET providers can implement async operations synchronously. Do not claim latency/scalability improvements without measuring the deployed driver/workload.

```csharp
db.CommandTimeout(30); // command timeout in seconds, retained for this session
using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
deadline.CancelAfter(TimeSpan.FromSeconds(10));

var hotels = await db.Select("hotels")
    .Where("city", "London")
    .Asc("id").Limit(100)
    .ReadAsListAsync<Hotel>(deadline.Token);
```

Negative timeout values throw; zero uses the provider's zero-timeout convention (commonly unlimited). If unset, the driver's default applies. Command timeout is not a connection-open timeout or a deadline for application callback work; configure the driver connection timeout and pass cancellation through callbacks too.

Important operational rules:

- Pass request/shutdown tokens through every terminal async call, not only the first query. Await cleanup before reusing/disposing the session.
- Do not use `.Result`, `.Wait()`, `async void` reader callbacks, or `Task.Run` as an async substitute.
- Prefer `ReadAsync` for an async callback: `Read(Action<...>)` cannot await an async lambda.
- Pre-cancellation avoids command execution. Cancellation during execution is cooperative and does not guarantee that an autocommit write did not persist.
- Errors/cancellation roll back **owned** transactions, not caller-owned ones. Already-committed chunks or external side effects remain.
- NetQL does not automatically retry. A single-use write builder is not a retry mechanism; recreating it does not make a duplicate write safe. Use unique business keys/idempotency records and deliberate retry/reconciliation policies.
- Do not log secrets or swallow database failures as “not found.” Return application-appropriate errors and collect sanitized diagnostics.

Legacy `Execute(callback)` calls the callback with the driver's affected-row count and returns the active transaction or `null`, **not the count**. If the callback throws after an autocommit write, the write may already be durable despite the method throwing. Prefer `ExecuteNonQuery` / `ExecuteAsync`; place post-write validations inside an explicit transaction when rollback is required.

## Application recipes

### Scoped async Web API

For a .NET 8 ASP.NET Core application, add the same project/SQLite references as the quick start. Set `ConnectionStrings__Default` through the environment or your secret/configuration store to a **development SQLite file** connection string (for example, `Data Source=/absolute/path/hotels.db`). Provision the quick-start `hotels` schema and data in that file separately. The in-memory console database is not shared with this API; do not use `:memory:` here without a deliberate shared-lifetime design.

Complete minimal `Program.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Daevsoft;
using Daevsoft.Lib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<NetQL>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    string connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Missing Default connection string.");
    var db = new NetQL(new SqliteConnection(connectionString), Provider.SQLite);
    db.CommandTimeout(30);
    return db;
});

var app = builder.Build();
app.MapGet("/hotels/{id:int}", GetHotel);
app.Run();

static async Task<IResult> GetHotel(
    int id, NetQL db, CancellationToken cancellationToken)
{
    if (id <= 0) return Results.BadRequest("id must be positive.");
    var hotel = await db.Select("id, name, room, city", "hotels")
        .Where("id", id)
        .ReadAsAsync<Hotel>(cancellationToken);
    return hotel == null ? Results.NotFound() : Results.Ok(hotel);
}

public class Hotel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Room { get; set; }
    public string? City { get; set; }
}
```

The request's cancellation token is bound to request abortion. The request scope owns/disposes the builder and its connection. Add authentication/authorization and tenant predicates for your application; the small example is not an authorization design. A repository can accept the same scoped `NetQL` and expose `Task<Hotel>` methods without creating/discarding another builder for every clause.

Do not register the builder or its connection as a singleton. Do not run parallel repository calls sharing this scoped instance; separate connections/scopes are required for independent parallel database work. If one service starts a transaction shared by several repositories, that service owns commit/rollback, not each repository method.

### Background service with a fresh scope per batch

A hosted service is long-lived; inject `IServiceScopeFactory`, **not a scoped NetQL instance**. This example assumes `audit_log(id, occurred_at)` with a unique `id`, an application-approved retention policy, and a suitable timestamp representation/index. It deletes at most a bounded selection per iteration, then releases the scope before waiting.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Daevsoft;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class AuditRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    public AuditRetentionWorker(IServiceScopeFactory scopes) => this.scopes = scopes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NetQL>();
                    DateTime cutoff = DateTime.UtcNow.AddDays(-90);
                    var ids = new List<long>();
                    await db.Select("id", "audit_log")
                        .Where("occurred_at", "<", cutoff)
                        .Asc("id").Limit(200)
                        .ReadAsync((reader, token) =>
                        {
                            ids.Add(reader.GetValue<long>(0));
                            return Task.CompletedTask;
                        }, stoppingToken);

                    if (ids.Count != 0)
                    {
                        await db.Delete("audit_log")
                            .WhereIn("id", ids.ToArray())
                            .Where("occurred_at", "<", cutoff) // recheck eligibility
                            .ExecuteAsync(stoppingToken);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown; the current scope has been disposed.
        }
    }
}
```

Register it alongside the scoped factory with `builder.Services.AddHostedService<AuditRetentionWorker>();`. Check parameter limits before increasing the batch size. This is idempotent retention work, not a general multi-worker job-claim algorithm: it does not provide exactly-once delivery or exclusive processing. Unexpected failures are not blindly retried here; configure host logging/supervision and a deliberate recovery policy. If each iteration contains several writes that must be atomic, put an owned transaction **inside that iteration's scope**.

### Streaming large results without materializing a list

`ReadAsync(Func<ReaderUtil<IDataReader>, CancellationToken, Task>, token)` advances the reader and **awaits each callback before reading the next row**. This enables bounded application memory and backpressure instead of `ReadAsListAsync` for large exports.

.NET 8 JSON Lines export method:

```csharp
public static async Task<int> ExportHotelsAsync(
    NetQL db, System.IO.Stream destination, CancellationToken cancellationToken)
{
    await using var writer = new System.IO.StreamWriter(
        destination, new System.Text.UTF8Encoding(false), 65536, leaveOpen: true);

    int rows = await db.Select("id, name, city", "hotels")
        .Asc("id")
        .ReadAsync(async (reader, token) =>
        {
            var row = new
            {
                id = reader.GetValue<int>("id"),
                name = reader.GetValue<string>("name"),
                city = reader.GetValue<string>("city")
            };
            string json = System.Text.Json.JsonSerializer.Serialize(row);
            await writer.WriteLineAsync(json.AsMemory(), token);
        }, cancellationToken);

    await writer.FlushAsync(cancellationToken);
    return rows;
}
```

The destination belongs to the caller. Do not retain the reader or schedule unawaited work using it; it represents the current row and is disposed on completion/failure. Do not issue another command on `db` inside the callback. Buffer a small page and then execute another command, or use a separate independent session if appropriate.

This avoids materializing the entire **result list**, not all provider buffering. `GetValue<byte[]>()` materializes an individual binary value; specialized large-LOB streaming may need direct provider APIs. Slow output holds the reader/connection longer. Cancellation/callback failure may leave a partially written export, and a database rollback cannot retract bytes already sent; choose an atomic-file or resumable-export strategy when needed.

### Keyset pagination and optional filters

For deep sequential pagination, seek after a stable unique key instead of repeatedly skipping rows:

```csharp
public static Task<List<Hotel>> GetHotelPageAsync(
    NetQL db, int afterId, int pageSize, string? city,
    CancellationToken cancellationToken)
{
    if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));
    if (pageSize < 1 || pageSize > 100)
        throw new ArgumentOutOfRangeException(nameof(pageSize));

    var query = db.Select("id, name, room, city", "hotels")
        .Where("id", ">", afterId);
    if (!string.IsNullOrWhiteSpace(city)) query.Where("city", city);
    return query.Asc("id").Limit(pageSize)
        .ReadAsListAsync<Hotel>(cancellationToken);
}
```

Return the last row's `Id` as the next cursor; keep filters unchanged across pages. This assumes positive, immutable IDs and a useful index. It does not provide a frozen snapshot under concurrent changes. For compound ordering such as `created_at, id`, seek with `(created_at > lastTime) OR (created_at = lastTime AND id > lastId)`, grouped with `Wrap` / `OrWrap`, and order by the same two keys. Define nullable-sort semantics explicitly.

Optional search filters can be appended before the terminal call:

```csharp
var query = db.Select("hotels");
if (!string.IsNullOrWhiteSpace(city)) query.Where("city", city);
if (minimumRooms.HasValue) query.Where("room", ">=", minimumRooms.Value);
if (!string.IsNullOrWhiteSpace(search))
    query.Where("name", "LIKE", "%" + search + "%");
var hotels = await query.Asc("name").Asc("id").Limit(100)
    .ReadAsListAsync<Hotel>(cancellationToken);
```

Parameterization prevents injection, but `%` and `_` in `search` remain wildcards. Use provider-specific escaping for literal substring semantics. Leading-wildcard searches may not use an ordinary index; inspect the execution plan instead of assuming the builder optimizes it.

### Master-detail, soft delete, and upsert boundaries

For master-detail reads, fetch the header, then its items sequentially. NetQL does not lazy-load them:

```csharp
var header = await db.Select("orders").Where("id", orderId)
    .ReadAsAsync<OrderHeader>(cancellationToken);
if (header != null)
{
    var items = await db.Select(
            new[] { "i.*", "p.name product_name" }, "order_items i")
        .Join("products p", "i.product_id", "p.id")
        .Where("i.order_id", orderId)
        .Asc("i.line_number").Asc("i.id")
        .ReadAsListAsync<OrderItem>(cancellationToken);
    // Build your response DTO from header and items.
}
```

Use appropriate isolation if the two reads must represent one consistent view. Avoid N+1 queries when listing many headers: fetch the related items in bounded sets or a suitable join.

Soft deletion is an explicit schema/application convention:

```csharp
int changed = await db.Update("hotels")
    .SetValue("deleted_at", DateTime.UtcNow)
    .Where("id", hotelId).AndNull("deleted_at")
    .ExecuteAsync(cancellationToken);

// Restore explicitly; add authorization and concurrency predicates as needed.
int restored = await db.Update("hotels")
    .SetValue("deleted_at", null, DbType.DateTime)
    .Where("id", hotelId)
    .ExecuteAsync(cancellationToken);
```

All relevant reads must explicitly include `.AndNull("deleted_at")`; there are no global query filters. Use the transactional guard pattern when a count mismatch must undo a change.

A check with `IsExist` followed by insert/update is **not an atomic upsert**. Concurrent writers can both observe absence. Enforce a database unique constraint and use provider-native upsert SQL or a deliberately designed conflict-handling strategy. Default-isolation transactions alone do not eliminate this race. NetQL does not translate upserts or return generated IDs through `Execute`.

## Mapping and reader behavior

Read mapping uses public writable instance properties and a public parameterless constructor for POCOs. `[Column("database_name")]` changes the mapping name; matching is case-insensitive. Read-only properties, indexers, and `[NotMapped]` properties are ignored on reads.

The factory and setters for each type are compiled once (expression trees) and cached, so repeated `ReadAs<T>()` / `ReadList<T>()` calls do not pay per-row reflection costs. Types that cannot be compiled—abstract/interface types, types without a public parameterless constructor, and structs (which are mapped through a boxed copy)—fall back to `Activator`/`PropertyInfo.SetValue` and still work.

```csharp
public class DocumentRow
{
    [Column("document_id")]
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public byte[]? Payload { get; set; }
    public decimal? Amount { get; set; }
    public DocumentState State { get; set; }
    [NotMapped]
    public string DisplayLabel => Title ?? "Untitled";
}

public enum DocumentState { Draft = 0, Published = 1 }
```

- Native reader conversion supports nullable types, numeric primitives, booleans, strings, dates, `Guid`, `byte[]`, and enums. `Guid` can come from a native value, string, or bytes; enums can come from names or underlying numeric values. `DateTimeOffset` and `TimeSpan` have conversion paths too. This does not mean every provider can store every CLR type identically.
- Conversions use invariant culture rather than parsing according to the machine's current culture. Timezone policy and provider-specific representations still belong to the application.
- Database null maps to `null` for nullable/reference types and `default(T)` for non-nullable value types. Choose nullable properties/scalars when null is meaningful; otherwise missing values can look like zero/false.
- A column absent from the result leaves a mapped property's initialized/default value unchanged. Named reader access to a missing column throws; `IsColumnExist(name)` can check first.
- Duplicate result names resolve to the first column in named access. Use explicit aliases or ordinal access.
- `reader.GetValue<T>(name)` and `reader.GetValue<T>(ordinal)` use the same conversion path as POCO mapping. A reader wrapper belongs to one result set.
- Unsupported nested objects and provider-specific types require explicit projections/conversion or direct ADO.NET; there is no relationship mapping.

Write mapping distinctions:

| Path | Mapping behavior |
|---|---|
| Explicit `AddValue` / `SetValue` | Use the supplied database column name and value/type |
| `Insert(...).Bulk(obj/rows)`, `Update(...).Bulk(obj)` | Honor `[Column]`, skip `[NotMapped]` and underscore-prefixed property/resolved column names; validate readable, non-indexed properties and unique columns |
| `Insert(table, singleObject)` | Same mapper as `.Bulk(object)`: honors `[Column]`, skips `[NotMapped]` and underscore-prefixed names, validates readable/non-indexed and unique columns |
| `BulkCopy(table, rows)` | Honors `[Column]`, skips `[NotMapped]` and underscore-prefixed names; all rows must share one CLR type; getters compiled once per type |

Bulk rejects malformed/indexed rows rather than trying to map them. Prefer separate read/write DTOs and omit generated/computed columns explicitly. `[Key]` / `[DatabaseGenerated]` do not provide automatic generated-column handling. String APIs such as `.Where("Id", id)` do **not** consult DTO `[Column]` attributes; use database column names.

Automatic parameter inference covers common primitive/nullable types, enums' underlying types, `Guid`, binary arrays, `DateTimeOffset`, and `TimeSpan`; fallback types use `DbType.String`. Override `DbType` when required, and test driver conversions rather than assuming schema validation.

## Migration notes

Intentional changes from older repository behavior:

| Older behavior or workaround | Current contract / action |
|---|---|
| Fluent read filters/joins/order/paging could be ignored | Reads execute full generated SQL; remove raw-SQL workarounds that existed solely for that defect, retaining application authorization predicates |
| `Select`/`Query` opened connections; preview queued parameters | Construction is offline; preview is pure/repeatable and can precede execution on the same builder |
| Pending GROUP BY/parameters could survive reuse | Operations clear statement state; `Query`/`Select` replace pending clauses/parameters |
| Code expected an automatic transaction per command | Default is no implicit transaction; explicitly call `Transaction` for atomic multi-command work |
| Rollback left owned transaction state behind | Owned completion disposes/clears transaction mode; begin a new transaction explicitly for the next unit |
| Caller transaction could be completed by NetQL | External ownership is enforced; owner commits/rolls back directly and owns the connection lifetime |
| Connection ownership was implicit everywhere | Default still owns; opt into borrowing with `leaveOpen: true`; use normal disposal, not forced close |
| `!!`/`Str.Raw` in a value was treated as SQL | All ordinary string values are literal; move intentional expressions to `AddRawValue` / `SetRawValue` / `WhereRaw` |
| Empty list paths produced inconsistent SQL/boolean operators | Empty IN is false, empty NOT IN is true, preserving AND/OR; review optional-filter logic |
| Invalid numeric limits removed limits or clamped offsets | Nonpositive lengths and negative offsets throw; SQL Server also requires explicit builder ordering |
| Reusing an executed write builder was possible | Writes are single-use; create another `SqlModel` for another write |
| Affected rows required a dynamic callback | Prefer `ExecuteNonQuery` / `ExecuteAsync`; legacy `Execute` still returns transaction/null |
| Core lacked read `Wrap` / some constructors | Both variants now expose grouping and the same constructor choices, including connection-string construction |
| Async required application-side wrappers | Native provider async APIs and cancellation are available; do not use `Task.Run` wrappers |
| Large Oracle `IN` lists failed with ORA-01795 | Lists above 1000 expressions are rendered as parenthesized chunks automatically; generated SQL shape changes only above that threshold |
| Oversized SQL Server bulk inserts failed inside the provider | Batches above 1000 rows or 2100 parameters are rejected with an actionable message before execution; pass a `chunkSize` to `Bulk` or use `BulkCopy` to split them |
| `ReadAs<T>` used reflection per column per row | Mapping factories/setters are compiled once per type and cached; structs fall back to boxed reflection but remain supported |
| Bulk loading meant hand-written `Chunk` loops or provider-specific code | `Bulk(rows, chunkSize)` chunks transactional SQL automatically; `BulkCopy`/`BulkCopyAsync` use the driver's native loader where available |

Review behavior changes in an isolated database before deployment, especially raw expressions, transaction ownership, filters, and persisted writes. Regression coverage is not proof that every existing application's SQL is portable or error-free.

## Testing and troubleshooting

### Safe regression suite

[`NetQL.RegressionTests/NetQL.RegressionTests.csproj`](NetQL.RegressionTests/NetQL.RegressionTests.csproj) targets .NET 8 and selects **one** library variant. It links only selected database-independent regression files from `TestDemo`, includes its own regression tests, and does **not** load the business `appsettings` files.

From the repository root:

```bash
dotnet test NetQL.RegressionTests/NetQL.RegressionTests.csproj
dotnet test NetQL.RegressionTests/NetQL.RegressionTests.csproj -p:NetQLVariant=Core
```

Current regression baseline: **155 passing test cases for each variant**. The suite covers session/statement cleanup, generated/executed SQL, literal/raw values, nulls, list predicates, write validation, compiled POCO mapping (including structs and init-only properties), chunked bulk writes (atomic, per-chunk, and caller-transaction modes), bulk copy orchestration (adapter seam, fallback, rollback, provider batch caps), ownership, async/cancellation, and provider formatting. Actual database execution is tested with **SQLite**; native bulk-copy adapters for the other providers are exercised only through fakes and SQL-generation assertions, **not verified live integration**. Run deployment-specific integration tests against your actual server/driver versions.

For library-only builds without database execution:

```bash
dotnet build netQL/NetQL.csproj
dotnet build NetQL.Core/NetQL.Core.csproj
```

Inspect warnings as well as errors; legacy-target and nullable warnings are not a supported-runtime upgrade. Keep shared changes synchronized and validate both variants separately.

### Legacy business-database tests: do not run casually

**`TestDemo` is not the safe regression entry point.** Its integration tests mutate seeded business tables and are not universally isolated or rolled back. Do not run an unqualified solution-wide `dotnet test`, the entire `TestDemo` project, or an arbitrary selected integration test against a production/shared business database.

For deliberately running a reviewed legacy integration test:

1. Inspect the test, mutations, expected schema, seed data, and cleanup first. Provision a disposable database with fixtures; an empty database is insufficient.
2. `TestDemo/Infrastructure/TestConfiguration.cs` loads required `appsettings.json`, optional `appsettings.Testing.json`, then environment variables. `ConnectionStrings__Default` overrides the connection string, but does not remove the requirement for the baseline JSON file.
3. Keep settings/credentials local or in the test secret store. Confirm the resolved destination before any execution; use a restricted test account.
4. Select only the intended tests and restore/reseed fixtures afterwards. Do not assume parallel tests sharing those fixtures are safe.

The isolated regression project avoids this configuration and business-data dependency entirely.

### Diagnostics and troubleshooting

Preview with `query.GenerateQuery()` or `write.GenerateQuery()` before execution. Preview returns SQL text, not parameter values or a query plan. `GenerateWhere(ignoreKeyword: true)` returns only conditions, not an executable statement. Use driver instrumentation to inspect **executed** SQL, parameter names/types, timing, and plans; redact values and connection strings.

| Symptom | Check |
|---|---|
| `Provider`, `Str`, or `Order` unresolved | Import `Daevsoft.Lib`; alias `QueryCommon.Order` |
| Duplicate/ambiguous library types | Reference only one variant |
| Unsupported provider or reflection driver load failure | Pass an explicit provider/connection; install the matching driver assembly in the host |
| Missing SQLite in-memory table on the next command | Default command completion closed the connection; use owned keep-alive for that database's intended lifetime |
| Unexpected result rows | Inspect full executed SQL, OR grouping, tenant predicates, null semantics, and join cardinality; fluent clauses are no longer intentionally ignored |
| Parameters missing after a new query | `Query`/`Select` replace prior parameters; add raw parameters afterwards |
| Duplicate parameter error | Make manually named parameters unique across nested queries; do not guess generated names |
| `!!NOW()` stored as text | Intentional literal-value behavior; use an explicit raw-value API for trusted SQL |
| Null assignment will not compile | Supply `SetValue<string>` or an explicit `DbType` overload |
| Missing/default mapped properties or conversion error | Check selected aliases, `[Column]`, writable properties, actual driver values, and nullable types |
| SQL Server paging rejected | Add explicit builder ordering with a stable key, or write complete native paging SQL |
| Bulk rejected or exceeds provider limits | Check nonempty compatible row shapes, explicit-vs-bulk mixing, generated columns, and chunk size; SQL Server rejects >1000 rows or >2100 parameters with an explicit message |
| Oracle rejects a long `IN` list | NetQL chunks >1000 expressions automatically; if the error persists, check the total bind count and split the query |
| “Operation is already running” / session cannot be modified | Await prior work; no parallel calls, callback re-entry, or concurrent disposal on one session |
| Clone cannot execute | Clones are for composition only; create a separate connection/session |
| Write cannot execute again | Create a new write builder; establish idempotency before retrying |
| Caller-owned transaction completion rejected | Complete the original transaction directly, not through NetQL |
| Transaction completed or connection closed | Check ownership and disposal order; do not reuse a wrapper attached to a completed external transaction |
| Async not supported or unexpectedly synchronous | Use a `DbConnection`/`DbCommand` provider; actual async I/O depends on its implementation |
| Exception after an autocommit write | The write may have persisted before callback/cleanup failed; reconcile rather than blindly retry |

When reporting an issue, include variant, consumer target, driver/server versions, a minimal schema/query, expected and actual behavior, and redacted SQL/parameter **names and types**. Never include credentials, tokens, production dumps, or sensitive values.

## Limitations and deployment checklist

NetQL deliberately leaves these responsibilities to the application/provider:

- No change tracking, relationship/lazy loading, migrations, schema validation, or automatic global tenant/soft-delete filters.
- No universal date/string/JSON function layer, upsert syntax, or generated-ID mechanism.
- Bulk paths are bounded by their documented limits: `Bulk(rows, chunkSize)` is still parameterized SQL (only SQL Server's row/parameter ceilings are capped automatically), and native `BulkCopy` adapters are reflective—driver-specific behavior, triggers, and identity handling remain the application's responsibility.
- No dedicated fluent HAVING/UNION/CTE/table-hint API; use complete parameterized SQL or direct ADO.NET as appropriate.
- No automatic retries, distributed atomicity, savepoint/nested-transaction abstraction, or general parallel-session safety.
- No guarantee that a cancellation means “nothing was written,” that rollback can undo external effects, or that an unsupported driver becomes compatible through a provider enum.
- No claim that every provider/version is live-tested, every query optimized, or every workload bug-free.

Before deployment:

1. Plan the EOL-framework migration and pin compatible driver/runtime versions.
2. Test actual provider SQL, type/null mapping, affected counts, transactions, cancellation, and cleanup against disposable fixtures.
3. Enforce allowlisted SQL structure, authorization predicates, constraints, least privilege, and secret-safe logs.
4. Use bounded projections/pages/batches and suitable indexes; measure plans, latency, allocations, lock duration, and connection-pool behavior.
5. Define transaction ownership, commit-outcome reconciliation, and write idempotency before adding retries or background parallelism.

Implementation references: [main entry point](netQL/NetQL.cs), [Core entry point](NetQL.Core/NetQL.cs), [sessions/query builder](netQL/Lib/DbUtils.cs), [async execution](netQL/Lib/DbUtils.Async.cs), [write builder](netQL/Lib/SqlModel.cs), [native bulk copy](netQL/Lib/BulkCopy.cs), [bulk-copy session API](netQL/Lib/DbUtils.BulkCopy.cs), [quoting/types](netQL/Lib/QueryCommon.cs), and [reader mapping](netQL/Lib/ReaderUtil.cs).

## License

[Apache License 2.0](LICENSE.txt)
