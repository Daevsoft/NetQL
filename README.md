# NetQL

**A lightweight, fluent SQL query builder for .NET.**  
Write type-safe `SELECT`, `INSERT`, `UPDATE`, and `DELETE` queries using a chainable C# API — no raw SQL strings required.

> **Author:** Muhamad Deva Arofi · [GitHub](https://github.com/daevsoft)  
> **License:** Apache 2.0  
> **Namespace:** `Daevsoft` / `Daevsoft.Core`

---

## Table of Contents

- [Supported Databases](#supported-databases)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
  - [Installation](#installation)
  - [Creating a Connection](#creating-a-connection)
- [Model Mapping](#model-mapping)
- [API Reference](#api-reference)
  - [SELECT](#select)
  - [INSERT](#insert)
  - [UPDATE](#update)
  - [DELETE](#delete)
  - [Existence Check](#existence-check)
  - [Raw SQL](#raw-sql)
- [WHERE Clauses](#where-clauses)
  - [Basic Where](#basic-where)
  - [Operators](#operators)
  - [OR Conditions](#or-conditions)
  - [WHERE IN / NOT IN](#where-in--not-in)
  - [Anonymous Object Where](#anonymous-object-where)
  - [Wrap (Grouped Conditions)](#wrap-grouped-conditions)
  - [IS NULL](#is-null)
  - [Raw Where](#raw-where)
  - [Custom Bind](#custom-bind)
  - [Subquery Where](#subquery-where)
- [Joins](#joins)
  - [INNER / LEFT / RIGHT](#inner--left--right)
  - [Raw ON Condition](#raw-on-condition)
  - [Subquery Joins](#subquery-joins)
- [Other Clauses](#other-clauses)
  - [GROUP BY](#group-by)
  - [ORDER BY](#order-by)
  - [LIMIT / OFFSET](#limit--offset)
- [Transactions](#transactions)
- [Connection Management](#connection-management)
- [Utilities](#utilities)
  - [Str.Raw](#strraw)
- [Common Use Cases & Recipes](#common-use-cases--recipes)
  - [Dependency Injection (ASP.NET Core)](#1-dependency-injection-aspnet-core)
  - [Repository Pattern](#2-repository-pattern)
  - [Paginated Listing with Search & Filter](#3-paginated-listing-with-search--filter)
  - [Dynamic Search with Multiple Optional Filters](#4-dynamic-search-with-multiple-optional-filters)
  - [Dashboard Reporting](#5-dashboard-reporting-aggregation--group-by)
  - [Master-Detail Query](#6-master-detail-query-order--order-items)
  - [Audit Trail / Activity Logging](#7-audit-trail--activity-logging)
  - [Soft Delete & Restore](#8-soft-delete--restore)
  - [Upsert Pattern](#9-upsert-pattern-check-then-insertupdate)
  - [Batch Data Migration](#10-batch-data-migration--bulk-processing)
  - [Complex Real-World Query](#11-subscription-reminder-complex-real-world-query)
  - [Quick Reference: Method → Generated SQL](#quick-reference-method--generated-sql)
- [Running Tests](#running-tests)

---

## Supported Databases

| Database   | Provider Enum          | Quote Style | Bind Symbol | Auto-detected Connection Type |
|------------|------------------------|-------------|-------------|-------------------------------|
| MySQL      | `Provider.MySql`       | `` ` ``     | `@`         | `MySqlConnection`             |
| PostgreSQL | `Provider.PostgreSQL`  | `"`         | `:`         | `NpgsqlConnection`            |
| Oracle     | `Provider.Oracle`      | `"`         | `:`         | `OracleConnection`            |
| SQL Server | `Provider.SqlServer`   | `[ ]`       | `@`         | `SqlConnection`               |
| SQLite     | *(auto-detect only)*   | *(space)*   | `@`         | `SqliteConnection`            |

> The provider is **auto-detected** from the `IDbConnection` type name when you use the single-argument constructor. You can also specify it explicitly.

---

## Project Structure

```
NetQL/
├── netQL/                    # Main library (target: net5.0)
│   ├── NetQL.cs              # Public entry point — inherits DbUtils
│   └── Lib/
│       ├── DbUtils.cs        # Core query builder (Select, Where, Join, Read, Execute, Transaction…)
│       ├── SqlModel.cs       # Insert / Update / Delete builder
│       ├── QueryCommon.cs    # Base class — quoting, type mapping, model classes
│       └── ReaderUtil.cs     # Typed data reader wrapper
├── NetQL.Core/               # .NET Core 3.1 variant (same API, namespace: Daevsoft.Core)
│   ├── NetQL.cs
│   └── Lib/                  # Same files as above
├── TestDemo/                 # MSTest integration tests (target: net8.0)
│   ├── QueryBuilder/         # Per-feature test classes
│   ├── Models/               # POCO models used in tests
│   ├── Infrastructure/       # Test DB factory / config
│   └── Utils/                # Randomize helper
├── netQL.sln                 # Solution file
└── README.md
```

---

## Getting Started

### Installation

Reference the `NetQL` project (or `NetQL.Core` for .NET Core 3.1) in your `.csproj`:

```xml
<ProjectReference Include="path\to\netQL\NetQL.csproj" />
```

### Creating a Connection

**Option 1 — Auto-detect provider from connection type:**

```csharp
using Daevsoft;
using Npgsql;

var connection = new NpgsqlConnection("Host=localhost;Database=mydb;Username=user;Password=pw");
var db = new NetQL(connection);  // auto-detects PostgreSQL
```

**Option 2 — Specify provider explicitly:**

```csharp
using Daevsoft;
using MySql.Data.MySqlClient;

var connection = new MySqlConnection("server=localhost;user=root;database=mydb;port=3306;password=pw");
var db = new NetQL(connection, Provider.MySql);
```

**Option 3 — Custom quoting / bind symbol:**

```csharp
var db = new NetQL(connection, quotSql: '"', bindSymbol: ':');
```

---

## Model Mapping

Query results are mapped to C# POCOs via reflection.  
Property names are matched to column names **case-insensitively**.

```csharp
public class Hotel
{
    public int ID { get; set; }
    public string Name { get; set; }
    public int Room { get; set; }
    public string City { get; set; }
}
```

Use the `[Column]` attribute when the database column name differs from the property name:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

public class Hotel
{
    [Column("room_id")]
    public int ID { get; set; }

    [Column("room_name")]
    public string Name { get; set; }

    [Column("no_room")]
    public int Room { get; set; }

    [Column("city_name")]
    public string City { get; set; }
}
```

### Supported Property Types

`string`, `int`, `long`, `short`, `double`, `decimal`, `bool`, `char`, `DateTime`  
— and their nullable equivalents (`int?`, `DateTime?`, etc.).

> **Convention:** Properties whose name starts with `_` are **skipped** during insert/bulk mapping.

---

## API Reference

### SELECT

**Select all rows:**

```csharp
List<Hotel> hotels = db.Select("table_hotel").ReadAsList<Hotel>();
```

**Select a single row:**

```csharp
Hotel hotel = db.Select("table_hotel")
    .Where("ID", 1)
    .ReadAs<Hotel>();
```

**Select specific columns:**

```csharp
var hotels = db.Select(new string[] { "name", "city" }, "table_hotel")
    .ReadAsList<Hotel>();
```

**Select with string columns (comma-separated):**

```csharp
var hotels = db.Select("name, city", "table_hotel")
    .ReadAsList<Hotel>();
```

**Read a single scalar value:**

```csharp
int count = db.Select(Str.Raw("COUNT(*)"), "table_hotel").ReadSingle<int>();
```

**Read a flat array of values:**

```csharp
IEnumerable<string> cities = db.Select("city", "table_hotel").ReadAsArray<string>();
```

**Low-level reader callback:**

```csharp
db.Select("table_hotel").Read(reader =>
{
    string name = reader.GetValue<string>("name");
    int room = reader.GetValue<int>("room");
});
```

---

### INSERT

**Insert with named values:**

```csharp
db.Insert("table_hotel")
    .AddValue("Name", "Refles")
    .AddValue("Room", 129)
    .AddValue("City", "Paris")
    .Execute();
```

**Insert with anonymous object (Bulk):**

```csharp
db.Insert("table_hotel")
    .Bulk(new { Name = "Vave", Room = 200, City = "Tokyo" })
    .Execute();
```

**Bulk insert multiple rows:**

```csharp
var rows = new List<object>
{
    new { Name = "Hotel A", Room = 50, City = "London" },
    new { Name = "Hotel B", Room = 80, City = "Paris" },
};

db.Insert("table_hotel").Bulk(rows).Execute();
```

**Insert with typed object:**

```csharp
var hotel = new Hotel { Name = "Grand", Room = 300, City = "Dubai" };
db.Insert("table_hotel", hotel).Execute();
```

**Insert with raw SQL values (e.g., database functions):**

```csharp
db.Insert("table_hotel")
    .AddValue("Name", "Auto")
    .AddValue("CreatedAt", Str.Raw("NOW()"))    // injected as raw SQL
    .AddValue("RowId", Str.Raw("get_row_id()")) // database function
    .Execute();
```

---

### UPDATE

**Update with SetValue:**

```csharp
db.Update("table_hotel")
    .SetValue("Name", "Vave Hotel")
    .SetValue("Room", 200)
    .Where("ID", 5)
    .Execute();
```

**Update with anonymous object (Bulk):**

```csharp
db.Update("table_hotel")
    .Bulk(new { Name = "Updated", Room = 150 })
    .Where("ID", 5)
    .Execute();
```

**Update with raw SET expression:**

```csharp
db.Update("mam_media_file")
    .SetRawValue("file_dir", "REPLACE(file_dir, :src, :dst)")
    .WhereRaw(Str.Raw("0"), "<", "POSITION(:src IN file_dir)")
    .AddParameter("src", "OLD_PATH")
    .AddParameter("dst", "NEW_PATH")
    .Execute();
```

---

### DELETE

```csharp
db.Delete("table_hotel").Where("ID", 2).Execute();
```

---

### Existence Check

```csharp
bool exists = db.Select("table_hotel")
    .Where("City", "London")
    .IsExist();
```

---

### Raw SQL

Run any arbitrary SQL statement via `Query()`:

```csharp
db.Query("SELECT * FROM table_hotel WHERE city = :city")
    .AddParameter("city", "London")
    .Read(reader =>
    {
        // process rows
    });

// Or for non-query commands:
db.Query("TRUNCATE TABLE temp_data").Execute();
```

Stored procedures:

```csharp
db.Query("sp_get_hotel_by_city", CommandType.StoredProcedure)
    .AddParameter("city", "London")
    .ReadAsList<Hotel>();
```

---

## WHERE Clauses

### Basic Where

```csharp
.Where("ID", 2)            // WHERE "ID" = @ID
```

### Operators

```csharp
.Where("Room", ">", 2)     // WHERE "Room" > @Room
.Where("CheckIn", ">=", DateTime.Now)
```

### OR Conditions

```csharp
.Where("City", "London")
.OrWhere("Room", 5)         // WHERE "City" = @City OR "Room" = @Room
```

### WHERE IN / NOT IN

**With array values:**

```csharp
.WhereIn("CityId", new int[] { 11, 12, 13 })
.WhereIn("CityName", new string[] { "Jakarta", "Bandung" })
.WhereNotIn("Status", new string[] { "CLOSED", "ARCHIVED" })
```

**With subquery:**

```csharp
.WhereIn("CityId", sub => sub
    .Select("CityId", "Hotels")
    .Where("Availability", true))
```

### Anonymous Object Where

Pass an anonymous object — each property becomes an AND condition:

```csharp
.Where(new { City = "London", Room = 5 })
// WHERE "City" = @City AND "Room" = @Room
```

### Wrap (Grouped Conditions)

Group conditions with parentheses:

```csharp
db.Select("me_config")
    .Where(new { category = "Digital Asset" })
    .Wrap(g => g
        .Where(new { module = "Services" })
        .OrWhere(new { module = "Audit Trail" })
    )
    .ReadAsList<MeConfig>();
// WHERE "category" = :category AND ( "module" = :module OR "module" = :module_1)
```

`OrWrap()` uses `OR` instead of `AND` to join the group.

### IS NULL

```csharp
.AndNull("DeletedAt")       // AND "DeletedAt" IS NULL
.OrNull("DeletedAt")        // OR  "DeletedAt" IS NULL
```

Passing `null` to `.Where()` automatically generates `IS NULL`:

```csharp
.Where("DeletedAt", null)   // AND "DeletedAt" IS NULL
```

### Raw Where

Bypass quoting and parameter binding:

```csharp
.WhereRaw("status = 'active'")                         // appended as-is
.WhereRaw("created_at", ">=", "current_timestamp")      // column op raw-value
```

### Custom Bind

Transform the bind placeholder in the generated SQL:

```csharp
.Where("Password", "secret123", x => "MD5(" + x + ")")
// WHERE "Password" = MD5(@Password)
```

### Subquery Where

```csharp
.Where("Price", "<", sub => sub
    .Select(Str.Raw("AVG(price)"), "table_hotel")
    .Where("City", "London"))
// WHERE "Price" < (SELECT AVG(price) FROM "table_hotel" WHERE "City" = :City)
```

---

## Joins

### INNER / LEFT / RIGHT

```csharp
// INNER JOIN
.Join("table_user user", "book.user_id", "user.id")

// LEFT JOIN
.LeftJoin("table_city city", "hotel.city_id", "city.id")

// RIGHT JOIN
.RightJoin("table_region region", "city.region_id", "region.id")
```

### Raw ON Condition

Pass a single string as the ON clause for complex join expressions:

```csharp
.Join("trx_qc_status_hdr tqs",
    "tqs.material_id=tqh.material_id AND tqs.revision_no=tqh.revision_no")
```

### Subquery Joins

Join against an inline subquery:

```csharp
var countryId = "IDN";

db.Select("book.*, city.name", "table_order book")
    .Join(sub => sub
        .Select("table_city")
        .Where("country", countryId)
        .Alias("city"),
        "book.city_id", "city.id")
    .Join("table_hotel hotel", "book.hotel_id", "hotel.id")
    .ReadAs<Booking>();
```

Subquery left/right joins are also available:

```csharp
.LeftJoin(sub => sub.Select("...").Alias("x"), "a.id", "x.id")
.RightJoin(sub => sub.Select("...").Alias("y"), "a.id", "y.id")
```

---

## Other Clauses

### GROUP BY

```csharp
.GroupBy("city_id")
.GroupBy(new List<string> { "city_id", "country_id" })
```

### ORDER BY

```csharp
.OrderBy("Name")                    // ASC (default)
.OrderBy("Name", Order.DESC)        // DESC
.Asc("Name")                        // shorthand ASC
.Desc("CreatedAt")                  // shorthand DESC
```

### LIMIT / OFFSET

```csharp
.Limit(10)                          // LIMIT 10
.Limit(start: 20, length: 10)       // LIMIT 10 OFFSET 20
```

---

## Transactions

Wrap multiple operations in a transaction — all succeed or all roll back:

```csharp
// Enable transaction mode
db.Transaction();

// Perform multiple operations
db.Insert("hotel")
    .Bulk(new { Name = "VIP 10A", Room = 10, City = "Jakarta" })
    .Execute();

db.Update("hotel")
    .SetValue("Name", "VIP 11B")
    .Where("ID", 1)
    .Execute();

db.Delete("hotel")
    .Where("ID", 2)
    .Execute();

// Commit all changes
db.Commit();
```

**Use an external `IDbTransaction`:**

```csharp
var txn = connection.BeginTransaction();
db.Transaction(txn);
// ... operations ...
db.Commit();
```

On any exception during `Execute()`, the transaction is automatically rolled back.

---

## Connection Management

| Method                  | Description                                                     |
|-------------------------|-----------------------------------------------------------------|
| `db.IsKeepAlive(true)`  | Keep the connection open between queries (batch mode).          |
| `db.Close()`            | Close the connection (respects keep-alive setting).             |
| `db.Close(true)`        | Force-close the connection regardless of keep-alive.            |
| `db.Dispose()`          | Dispose the underlying connection.                              |

> By default, NetQL opens and closes the connection **per operation**. Call `IsKeepAlive(true)` to reuse the same connection across multiple queries.

---

## Utilities

### Str.Raw

`Str.Raw(value)` marks a string so it's injected as **raw SQL** instead of being parameterized:

```csharp
// In SELECT columns
db.Select(Str.Raw("COUNT(id) as total"), "table_hotel")

// In WHERE values
.WhereRaw("created_at", ">=", "current_timestamp")

// In INSERT values
.AddValue("RowId", Str.Raw("gen_random_uuid()"))
```

> Internally, raw strings are prefixed with `!!` and stripped before SQL generation.

---

## Common Use Cases & Recipes

This section shows how professional developers typically integrate NetQL into real applications.

---

### 1. Dependency Injection (ASP.NET Core)

Register a single `NetQL` instance per HTTP request using `IDbConnection`:

```csharp
// Program.cs or Startup.cs
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = builder.Configuration.GetConnectionString("Default");
    return new NpgsqlConnection(connStr);
});

builder.Services.AddScoped<NetQL>(sp =>
{
    var connection = sp.GetRequiredService<IDbConnection>();
    return new NetQL(connection, Provider.PostgreSQL);
});
```

Then inject `NetQL` into any controller or service:

```csharp
[ApiController]
[Route("api/[controller]")]
public class HotelsController : ControllerBase
{
    private readonly NetQL _db;

    public HotelsController(NetQL db)
    {
        _db = db;
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var hotel = _db.Select("hotels")
            .Where("id", id)
            .ReadAs<Hotel>();

        return hotel != null ? Ok(hotel) : NotFound();
    }
}
```

---

### 2. Repository Pattern

Encapsulate data access behind a clean interface:

```csharp
public interface IHotelRepository
{
    Hotel GetById(int id);
    List<Hotel> GetByCity(string city);
    void Create(Hotel hotel);
    void Update(int id, Hotel hotel);
    void Delete(int id);
}

public class HotelRepository : IHotelRepository
{
    private readonly NetQL _db;

    public HotelRepository(NetQL db) => _db = db;

    public Hotel GetById(int id)
    {
        return _db.Select("hotels")
            .Where("id", id)
            .AndNull("deleted_at")       // soft-delete aware
            .ReadAs<Hotel>();
    }

    public List<Hotel> GetByCity(string city)
    {
        return _db.Select("hotels")
            .Where("city", city)
            .AndNull("deleted_at")
            .Asc("name")
            .ReadAsList<Hotel>();
    }

    public void Create(Hotel hotel)
    {
        _db.Insert("hotels")
            .AddValue("name", hotel.Name)
            .AddValue("room", hotel.Room)
            .AddValue("city", hotel.City)
            .AddValue("created_at", Str.Raw("NOW()"))
            .Execute();
    }

    public void Update(int id, Hotel hotel)
    {
        _db.Update("hotels")
            .SetValue("name", hotel.Name)
            .SetValue("room", hotel.Room)
            .SetValue("city", hotel.City)
            .SetValue("updated_at", Str.Raw("NOW()"))
            .Where("id", id)
            .Execute();
    }

    public void Delete(int id)
    {
        // Soft delete
        _db.Update("hotels")
            .SetValue("deleted_at", Str.Raw("NOW()"))
            .Where("id", id)
            .Execute();
    }
}
```

---

### 3. Paginated Listing with Search & Filter

A common API pattern — accepts page, page size, optional search keyword, and filter:

```csharp
public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public PagedResult<Hotel> GetHotels(string search, string city, int page = 1, int pageSize = 20)
{
    // --- Count total matching rows ---
    var countQuery = _db.Select(Str.Raw("COUNT(*) as total"), "hotels")
        .AndNull("deleted_at");

    if (!string.IsNullOrEmpty(city))
        countQuery = countQuery.Where("city", city);

    // For LIKE search, use WhereRaw
    if (!string.IsNullOrEmpty(search))
        countQuery = countQuery.WhereRaw(Str.Raw("LOWER(name)"), "LIKE", $"%{search.ToLower()}%");

    int totalCount = countQuery.ReadSingle<int>();

    // --- Fetch the page ---
    var dataQuery = _db.Select("hotels")
        .AndNull("deleted_at");

    if (!string.IsNullOrEmpty(city))
        dataQuery = dataQuery.Where("city", city);

    if (!string.IsNullOrEmpty(search))
        dataQuery = dataQuery.WhereRaw(Str.Raw("LOWER(name)"), "LIKE", $"%{search.ToLower()}%");

    var items = dataQuery
        .Asc("name")
        .Limit(start: (page - 1) * pageSize, length: pageSize)
        .ReadAsList<Hotel>();

    return new PagedResult<Hotel>
    {
        Items = items,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize
    };
}
```

---

### 4. Dynamic Search with Multiple Optional Filters

When the user can toggle filters on/off (e.g., a dashboard filter panel):

```csharp
public List<Order> SearchOrders(OrderFilter filter)
{
    var query = _db.Select(
        new string[] { "o.id", "o.order_date", "c.name customer_name", "o.total_amount" },
        "orders o"
    )
    .LeftJoin("customers c", "o.customer_id", "c.id")
    .AndNull("o.deleted_at");

    // Apply each filter only if provided
    if (filter.CustomerId.HasValue)
        query = query.Where("o.customer_id", filter.CustomerId.Value);

    if (filter.Status != null)
        query = query.Where("o.status", filter.Status);

    if (filter.DateFrom.HasValue)
        query = query.Where("o.order_date", ">=", filter.DateFrom.Value);

    if (filter.DateTo.HasValue)
        query = query.Where("o.order_date", "<=", filter.DateTo.Value);

    if (filter.MinAmount.HasValue)
        query = query.Where("o.total_amount", ">=", filter.MinAmount.Value);

    return query.Desc("o.order_date").Limit(100).ReadAsList<Order>();
}
```

---

### 5. Dashboard Reporting (Aggregation + GROUP BY)

Summarize data for charts or KPI cards:

```csharp
// Revenue by city
var revenueByCity = _db.Select(
        Str.Raw("city, COUNT(*) as total_bookings, SUM(total_amount) as revenue"),
        "bookings"
    )
    .Where("status", "CONFIRMED")
    .Where("booking_date", ">=", DateTime.Now.AddMonths(-3))
    .GroupBy("city")
    .Desc("revenue")
    .Limit(10)
    .ReadAsList<CityRevenue>();

// Monthly trend
var monthlyTrend = _db.Select(
        Str.Raw("DATE_TRUNC('month', created_at) as month, COUNT(*) as order_count"),
        "orders"
    )
    .Where("created_at", ">=", DateTime.Now.AddYears(-1))
    .GroupBy(Str.Raw("DATE_TRUNC('month', created_at)"))
    .OrderBy(Str.Raw("month"))
    .ReadAsList<MonthlyTrend>();
```

---

### 6. Master-Detail Query (Order + Order Items)

Fetch a parent record with its child records:

```csharp
public OrderDetail GetOrderDetail(string orderId)
{
    // Get order header
    var order = _db.Select("orders")
        .Where("id", orderId)
        .ReadAs<Order>();

    if (order == null) return null;

    // Get order items with product info
    var items = _db.Select(
            new string[] { "oi.*", "p.product_name", "p.sku" },
            "order_items oi"
        )
        .Join("products p", "oi.product_id", "p.id")
        .Where("oi.order_id", orderId)
        .Asc("oi.line_number")
        .ReadAsList<OrderItem>();

    return new OrderDetail { Order = order, Items = items };
}
```

---

### 7. Audit Trail / Activity Logging

Log every data change for compliance:

```csharp
public void UpdateWithAudit(string table, string recordId, object changes, string userId)
{
    _db.Transaction();

    try
    {
        // Perform the update
        _db.Update(table)
            .Bulk(changes)
            .Where("id", recordId)
            .Execute();

        // Log the change
        _db.Insert("audit_log")
            .AddValue("table_name", table)
            .AddValue("record_id", recordId)
            .AddValue("action", "UPDATE")
            .AddValue("changed_by", userId)
            .AddValue("changed_at", Str.Raw("NOW()"))
            .Execute();

        _db.Commit();
    }
    catch
    {
        _db.Rollback();
        throw;
    }
}
```

---

### 8. Soft Delete & Restore

A common pattern for applications that never physically delete records:

```csharp
// Soft-delete
public void SoftDelete(string table, string id, string deletedBy)
{
    _db.Update(table)
        .SetValue("deleted_at", Str.Raw("NOW()"))
        .SetValue("deleted_by", deletedBy)
        .Where("id", id)
        .AndNull("deleted_at")   // prevent double-delete
        .Execute();
}

// Restore
public void Restore(string table, string id)
{
    _db.Update(table)
        .SetRawValue("deleted_at", "NULL")
        .SetRawValue("deleted_by", "NULL")
        .Where("id", id)
        .Execute();
}

// Always filter out soft-deleted records
public List<T> GetActive<T>(string table)
{
    return _db.Select(table)
        .AndNull("deleted_at")
        .ReadAsList<T>();
}
```

---

### 9. Upsert Pattern (Check-then-Insert/Update)

When you need to insert a record if it doesn't exist, or update it if it does:

```csharp
public void UpsertConfig(string configCode, string category, string value)
{
    bool exists = _db.Select("app_config")
        .Where(new { config_code = configCode, category = category })
        .IsExist();

    if (exists)
    {
        _db.Update("app_config")
            .SetValue("config_value", value)
            .SetValue("updated_at", Str.Raw("NOW()"))
            .Where(new { config_code = configCode, category = category })
            .Execute();
    }
    else
    {
        _db.Insert("app_config")
            .Bulk(new
            {
                config_code = configCode,
                category = category,
                config_value = value,
                created_at = Str.Raw("NOW()")
            })
            .Execute();
    }
}
```

---

### 10. Batch Data Migration / Bulk Processing

Move or transform data between tables using transactions and bulk insert:

```csharp
public void MigrateMediaFiles(string sourceDir, string targetDir)
{
    _db.IsKeepAlive(true);
    _db.Transaction();

    try
    {
        // Batch update file paths
        _db.Update("media_files")
            .SetRawValue("file_dir", "REPLACE(file_dir, :src, :dst)")
            .WhereRaw(Str.Raw("0"), "<", "POSITION(:src IN file_dir)")
            .Where("file_type", "LOW")
            .AddParameter("src", sourceDir)
            .AddParameter("dst", targetDir)
            .Execute();

        // Log the migration
        _db.Insert("migration_log")
            .Bulk(new
            {
                source_dir = sourceDir,
                target_dir = targetDir,
                migrated_at = Str.Raw("NOW()"),
                status = "COMPLETED"
            })
            .Execute();

        _db.Commit();
    }
    catch
    {
        _db.Rollback();
        throw;
    }
    finally
    {
        _db.Close(true);
    }
}
```

---

### 11. Subscription Reminder (Complex Real-World Query)

A real example: find users whose subscriptions are expiring soon and haven't been notified yet, then batch-update their email status:

```csharp
_db.Update("UserSubscriptions")
    .SetValue("EmailSent", true)
    .WhereIn("UserId", sub => sub
        .Select(new string[] { "b.Id" }, "UserSubscriptions a")
        .Join("Users b", "a.UserId", "b.Id")
        .Where(
            Str.Raw("date_part('day', a.\"ExpiredDate\" - current_timestamp)"),
            "<=",
            configSub => configSub
                .Select(Str.Raw("cast(config_param as Int)"), "app_config")
                .Where("config_id", "EMAIL_REMIND_SUBSCRIPTION_BEFORE_DAY")
        )
        .Where("a.EmailSent", false)
        .WhereRaw("a.ExpiredDate", ">=", "current_timestamp")
        .GroupBy("b.Id")
    )
    .Where("Status", "ACTIVE")
    .Execute();
```

This generates SQL equivalent to:

```sql
UPDATE "UserSubscriptions"
SET "EmailSent" = :EmailSent
WHERE "UserId" IN (
    SELECT "b"."Id"
    FROM "UserSubscriptions" "a"
    INNER JOIN "Users" "b" ON "a"."UserId" = "b"."Id"
    WHERE date_part('day', a."ExpiredDate" - current_timestamp) <= (
        SELECT cast(config_param as Int)
        FROM "app_config"
        WHERE "config_id" = :config_id
    )
    AND "a"."EmailSent" = :EmailSent
    AND a."ExpiredDate" >= current_timestamp
    GROUP BY "b"."Id"
)
AND "Status" = :Status
```

---

### Quick Reference: Method → Generated SQL

| NetQL Code | Generated SQL |
|---|---|
| `.Select("hotels")` | `SELECT * FROM "hotels"` |
| `.Select(new[]{"name","city"}, "hotels")` | `SELECT "name","city" FROM "hotels"` |
| `.Select(Str.Raw("COUNT(*)"), "hotels")` | `SELECT COUNT(*) FROM "hotels"` |
| `.Where("id", 5)` | `WHERE "id" = :id` |
| `.Where("price", ">", 100)` | `WHERE "price" > :price` |
| `.Where(new { city = "London", active = true })` | `WHERE "city" = :city AND "active" = :active` |
| `.WhereIn("id", new[]{1,2,3})` | `WHERE "id" IN ('1','2','3')` |
| `.AndNull("deleted_at")` | `AND "deleted_at" IS NULL` |
| `.Join("users u","o.user_id","u.id")` | `INNER JOIN "users" "u" ON "o".user_id="u"."id"` |
| `.LeftJoin(...)` | `LEFT JOIN ...` |
| `.GroupBy("city")` | `GROUP BY "city"` |
| `.OrderBy("name", Order.DESC)` | `ORDER BY "name" DESC` |
| `.Limit(10)` | `LIMIT 10` |
| `.Limit(20, 10)` | `LIMIT 10 OFFSET 20` |
| `.Insert("hotels").AddValue("name","X")` | `INSERT INTO "hotels"("name") VALUES (:name)` |
| `.Update("hotels").SetValue("name","X")` | `UPDATE "hotels" SET "name"=:name` |
| `.Delete("hotels").Where("id",1)` | `DELETE FROM "hotels" WHERE "id"=:id` |

---

## Running Tests

The test project uses **MSTest** and targets **.NET 8.0**.  
Tests connect to a real PostgreSQL database configured via `appsettings.json` / `appsettings.Testing.json`.

```bash
cd TestDemo
dotnet test
```

---

## License

[Apache License 2.0](LICENSE.txt)
