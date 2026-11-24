using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for sales order management — order header + line items.
    /// Covers: master-detail queries, multi-table joins, order creation with transaction,
    /// status workflow, filtered listing, and order cancellation.
    /// </summary>
    [TestClass]
    public class SalesOrderTests
    {
        // ─── SELECT ────────────────────────────────────────────────────────

        [TestMethod]
        public void Select_Order_By_OrderNo()
        {
            var db = TestDbFactory.Create();

            var order = db.Select("sales_orders")
                .Where("order_no", "SO-2025-0001")
                .AndNull("deleted_at")
                .ReadAs<SalesOrder>();

            Assert.IsNotNull(order);
        }

        [TestMethod]
        public void Select_Order_With_Customer_Join()
        {
            var db = TestDbFactory.Create();

            var order = db.Select(
                    new string[] {
                        "o.order_id", "o.order_no", "o.order_date",
                        "o.total_amount", "o.status",
                        "c.customer_name", "c.email"
                    },
                    "sales_orders o"
                )
                .Join("customers c", "o.customer_id", "c.customer_id")
                .Where("o.order_no", "SO-2025-0001")
                .AndNull("o.deleted_at")
                .ReadAs<SalesOrder>();

            Assert.IsNotNull(order);
        }

        [TestMethod]
        public void Select_OrderLines_With_Product_Join()
        {
            var db = TestDbFactory.Create();

            var lines = db.Select(
                    new string[] {
                        "ol.*",
                        "p.product_name", "p.sku"
                    },
                    "sales_order_lines ol"
                )
                .Join("products p", "ol.product_id", "p.product_id")
                .Where("ol.order_id", "ORD001")
                .Asc("ol.line_number")
                .ReadAsList<SalesOrderLine>();

            Assert.IsNotNull(lines);
        }

        [TestMethod]
        public void Select_Orders_By_Status_And_DateRange()
        {
            var db = TestDbFactory.Create();

            var orders = db.Select("sales_orders")
                .Where("status", "CONFIRMED")
                .Where("order_date", ">=", new DateTime(2025, 1, 1))
                .Where("order_date", "<", new DateTime(2025, 7, 1))
                .AndNull("deleted_at")
                .Desc("order_date")
                .Limit(50)
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Orders_Multiple_Statuses()
        {
            var db = TestDbFactory.Create();

            // Fetch orders that are either pending or approved
            var orders = db.Select("sales_orders")
                .WhereIn("status", new string[] { "PENDING", "APPROVED" })
                .AndNull("deleted_at")
                .Desc("order_date")
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Orders_Exclude_Cancelled_And_Draft()
        {
            var db = TestDbFactory.Create();

            var orders = db.Select("sales_orders")
                .WhereNotIn("status", new string[] { "CANCELLED", "DRAFT" })
                .AndNull("deleted_at")
                .Desc("order_date")
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        [TestMethod]
        public void Select_Orders_By_Customer_City_Using_Subquery_Join()
        {
            var db = TestDbFactory.Create();

            // Join only customers from a specific country
            var countryCode = "IDN";

            var orders = db.Select(
                    new string[] { "o.*", "c.customer_name", "c.city" },
                    "sales_orders o"
                )
                .Join(sub => sub
                    .Select("customers")
                    .Where("country", countryCode)
                    .Alias("c"),
                    "o.customer_id", "c.customer_id")
                .Where("o.status", "CONFIRMED")
                .AndNull("o.deleted_at")
                .Desc("o.order_date")
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(orders);
        }

        // ─── INSERT (Transactional: Header + Lines) ────────────────────────

        [TestMethod]
        public void Insert_Order_With_Lines_Transactional()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                // Insert order header
                var orderId = "ORD-TEST-001";

                db.Insert("sales_orders")
                    .Bulk(new
                    {
                        order_id = orderId,
                        order_no = "SO-2025-TEST",
                        order_date = DateTime.Now,
                        customer_id = "CUST001",
                        status = "DRAFT",
                        total_amount = 0m,
                        currency_code = "USD",
                        created_by = "SYSTEM",
                        created_at = Str.Raw("NOW()")
                    })
                    .Execute();

                // Insert line items
                var lines = new List<object>
                {
                    new
                    {
                        line_id = Str.Raw("gen_random_uuid()"),
                        order_id = orderId,
                        line_number = 1,
                        product_id = "PROD001",
                        qty = 10,
                        unit_price = 25.50m,
                        discount = 0m,
                        line_total = 255.00m
                    },
                    new
                    {
                        line_id = Str.Raw("gen_random_uuid()"),
                        order_id = orderId,
                        line_number = 2,
                        product_id = "PROD002",
                        qty = 5,
                        unit_price = 100.00m,
                        discount = 10.00m,
                        line_total = 490.00m
                    }
                };

                db.Insert("sales_order_lines").Bulk(lines).Execute();

                // Update order total from lines
                db.Update("sales_orders")
                    .SetRawValue("total_amount",
                        "(SELECT COALESCE(SUM(line_total), 0) FROM sales_order_lines WHERE order_id = '" + orderId + "')")
                    .Where("order_id", orderId)
                    .Execute();

                db.Commit();
                Assert.IsTrue(true);
            }
            catch
            {
                db.Rollback();
                throw;
            }
            finally
            {
                db.Close(true);
            }
        }

        // ─── UPDATE (Status Workflow) ──────────────────────────────────────

        [TestMethod]
        public void Update_Order_Status_Submit_For_Approval()
        {
            var db = TestDbFactory.Create();

            db.Update("sales_orders")
                .SetValue("status", "PENDING")
                .SetValue("updated_at", Str.Raw("NOW()"))
                .Where("order_id", "ORD001")
                .Where("status", "DRAFT") // can only submit from DRAFT
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Update_Order_Approve()
        {
            var db = TestDbFactory.Create();

            db.Update("sales_orders")
                .SetValue("status", "APPROVED")
                .SetValue("approved_by", "MGR001")
                .SetValue("approved_at", Str.Raw("NOW()"))
                .Where("order_id", "ORD001")
                .Where("status", "PENDING") // can only approve from PENDING
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── SOFT DELETE ───────────────────────────────────────────────────

        [TestMethod]
        public void Soft_Delete_Cancelled_Order()
        {
            var db = TestDbFactory.Create();

            db.Transaction();

            db.Update("sales_orders")
                .SetValue("status", "CANCELLED")
                .SetValue("deleted_at", Str.Raw("NOW()"))
                .Where("order_id", "ORD001")
                .WhereIn("status", new string[] { "DRAFT", "PENDING" }) // can only cancel DRAFT/PENDING
                .Execute();

            // Also soft-delete line items
            db.Update("sales_order_lines")
                .SetRawValue("deleted_at", Str.Raw("NOW()").ToString())
                .Where("order_id", "ORD001")
                .Execute();

            db.Commit();

            Assert.IsTrue(true);
        }
    }
}
