using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for enterprise reporting and aggregation queries.
    /// Covers: GROUP BY, SUM, COUNT, AVG, date-based aggregation,
    /// top-N ranking, and multi-table report joins.
    /// </summary>
    [TestClass]
    public class ReportingTests
    {
        [TestMethod]
        public void Report_Sales_Revenue_By_Customer()
        {
            var db = TestDbFactory.Create();

            var report = db.Select(
                    Str.Raw("c.customer_name as label, COUNT(o.order_id) as total_count, SUM(o.total_amount) as total_amount"),
                    "sales_orders o"
                )
                .Join("customers c", "o.customer_id", "c.customer_id")
                .Where("o.status", "CONFIRMED")
                .AndNull("o.deleted_at")
                .GroupBy("c.customer_name")
                .Desc(Str.Raw("total_amount"))
                .Limit(10)
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(report);
        }

        [TestMethod]
        public void Report_Monthly_Sales_Trend()
        {
            var db = TestDbFactory.Create();

            var trend = db.Select(
                    Str.Raw("TO_CHAR(order_date, 'YYYY-MM') as label, COUNT(*) as total_count, SUM(total_amount) as total_amount"),
                    "sales_orders"
                )
                .Where("order_date", ">=", DateTime.Now.AddYears(-1))
                .WhereNotIn("status", new string[] { "CANCELLED", "DRAFT" })
                .AndNull("deleted_at")
                .GroupBy(Str.Raw("TO_CHAR(order_date, 'YYYY-MM')"))
                .OrderBy(Str.Raw("label"))
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(trend);
        }

        [TestMethod]
        public void Report_Employee_Count_By_Department()
        {
            var db = TestDbFactory.Create();

            var report = db.Select(
                    Str.Raw("d.department_name as label, COUNT(e.employee_id) as total_count, 0 as total_amount"),
                    "employees e"
                )
                .Join("departments d", "e.department_id", "d.department_id")
                .Where("e.is_active", true)
                .AndNull("e.deleted_at")
                .GroupBy("d.department_name")
                .Desc(Str.Raw("total_count"))
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(report);
        }

        [TestMethod]
        public void Report_Top_Selling_Products()
        {
            var db = TestDbFactory.Create();

            var topProducts = db.Select(
                    Str.Raw("p.product_name as label, SUM(ol.qty) as total_count, SUM(ol.line_total) as total_amount"),
                    "sales_order_lines ol"
                )
                .Join("products p", "ol.product_id", "p.product_id")
                .Join("sales_orders o", "ol.order_id", "o.order_id")
                .Where("o.status", "CONFIRMED")
                .AndNull("o.deleted_at")
                .GroupBy("p.product_name")
                .Desc(Str.Raw("total_amount"))
                .Limit(10)
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(topProducts);
        }

        [TestMethod]
        public void Report_Stock_Movement_Summary()
        {
            var db = TestDbFactory.Create();

            var summary = db.Select(
                    Str.Raw("movement_type as label, COUNT(*) as total_count, SUM(qty) as total_amount"),
                    "stock_movements"
                )
                .Where("movement_date", ">=", DateTime.Now.AddMonths(-1))
                .GroupBy("movement_type")
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(summary);
        }

        [TestMethod]
        public void Report_Orders_Above_Average_Amount()
        {
            var db = TestDbFactory.Create();

            // Orders whose total is above the overall average
            var aboveAvg = db.Select("sales_orders")
                .Where("total_amount", ">", sub => sub
                    .Select(Str.Raw("AVG(total_amount)"), "sales_orders")
                    .Where("status", "CONFIRMED")
                    .AndNull("deleted_at"))
                .Where("status", "CONFIRMED")
                .AndNull("deleted_at")
                .Desc("total_amount")
                .Limit(20)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(aboveAvg);
        }

        [TestMethod]
        public void Report_Customer_Order_Summary_With_Having_Equivalent()
        {
            var db = TestDbFactory.Create();

            // Customers with more than 5 orders — using subquery approach
            // since NetQL doesn't have HAVING, we wrap the aggregation as a subquery
            var bigCustomers = db.Select(
                    Str.Raw("customer_id as label, COUNT(*) as total_count, SUM(total_amount) as total_amount"),
                    "sales_orders"
                )
                .Where("status", "CONFIRMED")
                .AndNull("deleted_at")
                .GroupBy("customer_id")
                .ReadAsList<ReportRow>();

            // Filter in application layer for HAVING equivalent
            var filtered = bigCustomers.Where(r => r.TotalCount > 5).ToList();

            Assert.IsNotNull(filtered);
        }

        [TestMethod]
        public void Report_Daily_New_Employees()
        {
            var db = TestDbFactory.Create();

            var dailyHires = db.Select(
                    Str.Raw("hire_date::date as label, COUNT(*) as total_count, 0 as total_amount"),
                    "employees"
                )
                .Where("hire_date", ">=", DateTime.Now.AddDays(-30))
                .GroupBy(Str.Raw("hire_date::date"))
                .OrderBy(Str.Raw("label"))
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(dailyHires);
        }
    }
}
