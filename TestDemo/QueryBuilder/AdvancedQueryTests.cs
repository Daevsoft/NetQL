using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for advanced query patterns commonly used in enterprise backends.
    /// Covers: complex WHERE with multiple Wrap groups, multi-table subquery joins,
    /// raw SQL with AddParameter, EXISTS subqueries, scalar aggregation reads,
    /// and real-world data migration / batch update scenarios.
    /// </summary>
    [TestClass]
    public class AdvancedQueryTests
    {
        // ─── COMPLEX WHERE GROUPING ────────────────────────────────────────

        [TestMethod]
        public void Complex_Where_With_Nested_Wrap_Groups()
        {
            var db = TestDbFactory.Create();

            // WHERE category = 'A' AND ( (status = 'ACTIVE' AND qty > 0) OR (status = 'RESERVED') )
            var products = db.Select("products")
                .Where("category_id", "CAT_A")
                .Wrap(outer => outer
                    .Wrap(inner => inner
                        .Where("status", "ACTIVE")
                        .Where("stock_qty", ">", 0)
                    )
                    .OrWrap(inner => inner
                        .Where("status", "RESERVED")
                    )
                )
                .AndNull("deleted_at")
                .ReadAsList<Product>();

            Assert.IsNotNull(products);
        }

        [TestMethod]
        public void Where_With_AnonymousObject_And_OrWhere_Combined()
        {
            var db = TestDbFactory.Create();

            var employees = db.Select("employees")
                .Where(new { department_id = "DEPT_IT", is_active = true })
                .Wrap(g => g
                    .Where(new { position_id = "POS_DEV" })
                    .OrWhere(new { position_id = "POS_LEAD" })
                    .OrWhere(new { position_id = "POS_MGR" })
                )
                .AndNull("deleted_at")
                .ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }

        // ─── SUBQUERY IN SELECT ────────────────────────────────────────────

        [TestMethod]
        public void WhereIn_With_Subquery_Active_Customers()
        {
            var db = TestDbFactory.Create();

            // Orders from customers who placed at least one order this year
            var orders = db.Select("sales_orders")
                .WhereIn("customer_id", sub => sub
                    .Select("customer_id", "sales_orders")
                    .Where("order_date", ">=", new DateTime(DateTime.Now.Year, 1, 1))
                    .Where("status", "CONFIRMED")
                    .GroupBy("customer_id"))
                .AndNull("deleted_at")
                .Desc("order_date")
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void WhereNotIn_Exclude_Specific_Records()
        {
            var db = TestDbFactory.Create();

            // Products not in any pending order
            var available = db.Select("products")
                .WhereNotIn("product_id", sub => sub
                    .Select("product_id", "sales_order_lines ol")
                    .Join("sales_orders o", "ol.order_id", "o.order_id")
                    .WhereIn("o.status", new string[] { "PENDING", "PROCESSING" }))
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAsList<Product>();

            Assert.IsNotNull(available);
        }

        // ─── SUBQUERY JOIN ─────────────────────────────────────────────────

        [TestMethod]
        public void Join_Subquery_With_Alias()
        {
            var db = TestDbFactory.Create();

            // Join orders with a subquery that computes each customer's total order count
            var results = db.Select(
                    new string[] { "o.*", "cs.order_count" },
                    "sales_orders o"
                )
                .Join(sub => sub
                    .Select(
                        Str.Raw("customer_id, COUNT(*) as order_count"),
                        "sales_orders"
                    )
                    .Where("status", "CONFIRMED")
                    .GroupBy("customer_id")
                    .Alias("cs"),
                    "o.customer_id", "cs.customer_id")
                .Where("o.status", "CONFIRMED")
                .Desc("o.order_date")
                .Limit(20)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(results);
        }

        [TestMethod]
        public void LeftJoin_Subquery_Products_With_Last_Movement()
        {
            var db = TestDbFactory.Create();

            // Products LEFT JOINed with their latest stock movement date
            var results = db.Select(
                    new string[] { "p.*", "lm.last_movement" },
                    "products p"
                )
                .LeftJoin(sub => sub
                    .Select(
                        Str.Raw("product_id, MAX(movement_date) as last_movement"),
                        "stock_movements"
                    )
                    .GroupBy("product_id")
                    .Alias("lm"),
                    "p.product_id", "lm.product_id")
                .Where("p.is_active", true)
                .AndNull("p.deleted_at")
                .ReadAsList<Product>();

            Assert.IsNotNull(results);
        }

        // ─── RAW SQL WITH PARAMETERS ───────────────────────────────────────

        [TestMethod]
        public void Raw_Query_With_AddParameter()
        {
            var db = TestDbFactory.Create();

            var results = db.Query(
                    "SELECT * FROM sales_orders WHERE status = :status AND order_date >= :fromDate ORDER BY order_date DESC LIMIT 10"
                )
                .AddParameter("status", "CONFIRMED")
                .AddParameter("fromDate", new DateTime(2025, 1, 1))
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(results);
        }

        [TestMethod]
        public void Update_With_Raw_Expression_And_Parameters()
        {
            var db = TestDbFactory.Create();

            // Replace part of a file path using REPLACE() with bind parameters
            db.Update("media_files")
                .SetRawValue("file_path", "REPLACE(file_path, :oldPath, :newPath)")
                .WhereRaw(Str.Raw("0"), "<", "POSITION(:oldPath IN file_path)")
                .Where("file_type", "IMAGE")
                .AddParameter("oldPath", "\\\\old-server\\share")
                .AddParameter("newPath", "\\\\new-server\\share")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── SCALAR READS ──────────────────────────────────────────────────

        [TestMethod]
        public void ReadSingle_Scalar_Count()
        {
            var db = TestDbFactory.Create();

            int count = db.Select(Str.Raw("COUNT(*)"), "employees")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadSingle<int>();

            Assert.IsTrue(count >= 0);
        }

        [TestMethod]
        public void ReadSingle_Scalar_Sum()
        {
            var db = TestDbFactory.Create();

            decimal totalRevenue = db.Select(Str.Raw("COALESCE(SUM(total_amount), 0)"), "sales_orders")
                .Where("status", "CONFIRMED")
                .Where("order_date", ">=", new DateTime(2025, 1, 1))
                .AndNull("deleted_at")
                .ReadSingle<decimal>();

            Assert.IsTrue(totalRevenue >= 0);
        }

        [TestMethod]
        public void ReadSingle_Scalar_Max_Date()
        {
            var db = TestDbFactory.Create();

            string lastOrderDate = db.Select(Str.Raw("MAX(order_date)::text"), "sales_orders")
                .Where("status", "CONFIRMED")
                .ReadSingle<string>();

            // Can be null if no orders
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void ReadAsArray_Column_Values()
        {
            var db = TestDbFactory.Create();

            var departments = db.Select("department_id", "employees")
                .Where("is_active", true)
                .GroupBy("department_id")
                .ReadAsArray<string>();

            Assert.IsNotNull(departments);
        }

        // ─── EXISTENCE CHECK PATTERNS ──────────────────────────────────────

        [TestMethod]
        public void IsExist_Before_Insert_Guard()
        {
            var db = TestDbFactory.Create();

            // Prevent duplicate employee code
            bool alreadyExists = db.Select("employees")
                .Where("employee_code", "EMP-UNIQUE-001")
                .IsExist();

            if (!alreadyExists)
            {
                db.Insert("employees")
                    .Bulk(new
                    {
                        employee_id = Str.Raw("gen_random_uuid()"),
                        employee_code = "EMP-UNIQUE-001",
                        full_name = "Unique Check Test",
                        department_id = "DEPT_IT",
                        is_active = true,
                        created_at = Str.Raw("NOW()")
                    })
                    .Execute();
            }

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void IsExist_With_AnonymousObject_Where()
        {
            var db = TestDbFactory.Create();

            bool exists = db.Select("app_config")
                .Where(new
                {
                    config_code = "SMTP_HOST",
                    category = "Email",
                    module = "Notification"
                })
                .IsExist();

            // Just verify it runs without exception
            Assert.IsTrue(true);
        }

        // ─── BATCH DATA MIGRATION ──────────────────────────────────────────

        [TestMethod]
        public void Batch_Update_Employee_Salary_By_Department()
        {
            var db = TestDbFactory.Create();

            // 5% raise for all active IT employees
            db.Update("employees")
                .SetRawValue("salary", "salary * 1.05")
                .SetValue("updated_at", Str.Raw("NOW()"))
                .Where("department_id", "DEPT_IT")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Batch_Deactivate_Expired_Users()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                // Mark users as inactive if all their subscriptions are expired
                db.Update("users")
                    .SetValue("is_active", false)
                    .SetValue("deleted_at", Str.Raw("NOW()"))
                    .WhereNotIn("user_id", sub => sub
                        .Select("user_id", "user_subscriptions")
                        .Where("status", "ACTIVE")
                        .WhereRaw("expired_date", ">=", "current_timestamp"))
                    .Where("is_active", true)
                    .AndNull("deleted_at")
                    .Execute();

                // Log it
                db.Insert("audit_logs")
                    .Bulk(new
                    {
                        audit_id = Str.Raw("gen_random_uuid()"),
                        table_name = "users",
                        record_id = "BATCH",
                        action = "BATCH_DEACTIVATE",
                        changed_by = "SYSTEM",
                        changed_at = Str.Raw("NOW()"),
                        new_values = "{\"reason\":\"All subscriptions expired\"}"
                    })
                    .Execute();

                db.Commit();
                Assert.IsTrue(true);
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        // ─── RAW JOIN ON CONDITION ─────────────────────────────────────────

        [TestMethod]
        public void Join_With_Raw_On_MultiColumn_Condition()
        {
            var db = TestDbFactory.Create();

            // Join on a composite key using raw ON clause
            var results = db.Select(
                    Str.Raw("sm.*, p.product_name"),
                    "stock_movements sm"
                )
                .Join("products p",
                    "sm.product_id=p.product_id AND sm.warehouse_id='WH_MAIN'")
                .Where("sm.movement_type", "OUT")
                .Desc("sm.movement_date")
                .Limit(50)
                .ReadAsList<StockMovement>();

            Assert.IsNotNull(results);
        }

        [TestMethod]
        public void Select_With_DbType_Override()
        {
            var db = TestDbFactory.Create();

            // Use DbType parameter for numeric columns that need explicit typing
            var results = db.Select(
                    Str.Raw("sme.row_id_epi, tqh.*"),
                    "trx_qc_technical_hist tqh"
                )
                .Join("stock_material_epi sme", "tqh.material_id", "sme.material_id")
                .Where("sme.row_id_epi", "MMMMMMMMMMM1700945")
                .Where("tqh.revision_no", 10, System.Data.DbType.VarNumeric)
                .ReadAs<MediaQCTrans>();

            // May return null if data doesn't exist — just verify no exception
            Assert.IsTrue(true);
        }
    }
}
