using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for paginated, filtered, and search-based listing queries.
    /// Covers: pagination with LIMIT/OFFSET, dynamic filters, keyword search,
    /// total count, and conditional WHERE chaining.
    /// </summary>
    [TestClass]
    public class PaginationFilterTests
    {
        [TestMethod]
        public void Select_Paginated_First_Page()
        {
            var db = TestDbFactory.Create();

            var page = 1;
            var pageSize = 20;

            var orders = db.Select("sales_orders")
                .AndNull("deleted_at")
                .Desc("order_date")
                .Limit(start: (page - 1) * pageSize, length: pageSize)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Paginated_Third_Page()
        {
            var db = TestDbFactory.Create();

            var page = 3;
            var pageSize = 10;

            var orders = db.Select("sales_orders")
                .AndNull("deleted_at")
                .Desc("order_date")
                .Limit(start: (page - 1) * pageSize, length: pageSize)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Count_For_Pagination()
        {
            var db = TestDbFactory.Create();

            int totalCount = db.Select(Str.Raw("COUNT(*) as total"), "sales_orders")
                .AndNull("deleted_at")
                .ReadSingle<int>();

            Assert.IsTrue(totalCount >= 0);
        }

        [TestMethod]
        public void Select_Count_With_Filter()
        {
            var db = TestDbFactory.Create();

            int totalCount = db.Select(Str.Raw("COUNT(*) as total"), "sales_orders")
                .Where("status", "CONFIRMED")
                .Where("order_date", ">=", new DateTime(2025, 1, 1))
                .AndNull("deleted_at")
                .ReadSingle<int>();

            Assert.IsTrue(totalCount >= 0);
        }

        [TestMethod]
        public void Select_With_Optional_Status_Filter()
        {
            var db = TestDbFactory.Create();

            // Simulate: status filter may or may not be provided
            string? statusFilter = "CONFIRMED";

            var query = db.Select("sales_orders")
                .AndNull("deleted_at");

            if (!string.IsNullOrEmpty(statusFilter))
                query = query.Where("status", statusFilter);

            var orders = query
                .Desc("order_date")
                .Limit(20)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_With_Optional_DateRange_Filter()
        {
            var db = TestDbFactory.Create();

            DateTime? dateFrom = new DateTime(2025, 1, 1);
            DateTime? dateTo = null; // not provided

            var query = db.Select("sales_orders")
                .AndNull("deleted_at");

            if (dateFrom.HasValue)
                query = query.Where("order_date", ">=", dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where("order_date", "<=", dateTo.Value);

            var orders = query.Desc("order_date").Limit(50).ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_With_Multiple_Optional_Filters()
        {
            var db = TestDbFactory.Create();

            // Simulate a dashboard filter panel
            string? customerId = "CUST001";
            string? status = null;
            DateTime? dateFrom = new DateTime(2025, 6, 1);
            DateTime? dateTo = new DateTime(2025, 12, 31);
            decimal? minAmount = 1000m;

            var query = db.Select(
                    new string[] { "o.*", "c.customer_name" },
                    "sales_orders o"
                )
                .LeftJoin("customers c", "o.customer_id", "c.customer_id")
                .AndNull("o.deleted_at");

            if (!string.IsNullOrEmpty(customerId))
                query = query.Where("o.customer_id", customerId);

            if (!string.IsNullOrEmpty(status))
                query = query.Where("o.status", status);

            if (dateFrom.HasValue)
                query = query.Where("o.order_date", ">=", dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where("o.order_date", "<=", dateTo.Value);

            if (minAmount.HasValue)
                query = query.Where("o.total_amount", ">=", minAmount.Value);

            var orders = query.Desc("o.order_date").Limit(100).ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Keyword_Search_Across_Columns()
        {
            var db = TestDbFactory.Create();

            var keyword = "john";

            var customers = db.Select("customers")
                .Wrap(g => g
                    .WhereRaw(Str.Raw("LOWER(customer_name)"), "LIKE", $"%{keyword.ToLower()}%")
                    .OrWhereRaw(Str.Raw("LOWER(email)"), "LIKE", $"%{keyword.ToLower()}%")
                    .OrWhereRaw(Str.Raw("LOWER(phone)"), "LIKE", $"%{keyword.ToLower()}%")
                )
                .Where("is_active", true)
                .Limit(20)
                .ReadAsList<Customer>();

            Assert.IsNotNull(customers);
        }

        [TestMethod]
        public void Select_Employees_Search_With_Department_Filter()
        {
            var db = TestDbFactory.Create();

            string? keyword = "dev";
            string? departmentId = "DEPT_IT";

            var query = db.Select(
                    new string[] { "e.*", "d.department_name" },
                    "employees e"
                )
                .Join("departments d", "e.department_id", "d.department_id")
                .Where("e.is_active", true)
                .AndNull("e.deleted_at");

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Wrap(g => g
                    .WhereRaw(Str.Raw("LOWER(e.full_name)"), "LIKE", $"%{keyword.ToLower()}%")
                    .OrWhereRaw(Str.Raw("LOWER(e.email)"), "LIKE", $"%{keyword.ToLower()}%")
                    .OrWhereRaw(Str.Raw("LOWER(e.employee_code)"), "LIKE", $"%{keyword.ToLower()}%")
                );
            }

            if (!string.IsNullOrEmpty(departmentId))
                query = query.Where("e.department_id", departmentId);

            var employees = query.Asc("e.full_name").Limit(20).ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }
    }
}
