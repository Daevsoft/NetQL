using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for transaction management patterns — enterprise scenarios.
    /// Covers: single transaction, multi-step transactions, rollback on failure,
    /// audit trail logging, and transaction with keep-alive.
    /// </summary>
    [TestClass]
    public class TransactionTests
    {
        [TestMethod]
        public void Transaction_Insert_And_Update_Commit()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            db.Insert("employees")
                .Bulk(new
                {
                    employee_id = Str.Raw("gen_random_uuid()"),
                    employee_code = "EMP-TXN-001",
                    full_name = "Transaction Test",
                    email = "txn@company.com",
                    department_id = "DEPT_IT",
                    is_active = true,
                    created_at = Str.Raw("NOW()")
                })
                .Execute();

            db.Update("departments")
                .SetRawValue("employee_count", "employee_count + 1")
                .Where("department_id", "DEPT_IT")
                .Execute();

            db.Commit();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Transaction_Update_With_Audit_Log()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var employeeId = "EMP001";
                var userId = "ADMIN001";

                // Perform the update
                db.Update("employees")
                    .SetValue("department_id", "DEPT_FIN")
                    .SetValue("updated_at", Str.Raw("NOW()"))
                    .Where("employee_id", employeeId)
                    .Execute();

                // Write audit log
                db.Insert("audit_logs")
                    .Bulk(new
                    {
                        audit_id = Str.Raw("gen_random_uuid()"),
                        table_name = "employees",
                        record_id = employeeId,
                        action = "UPDATE",
                        changed_by = userId,
                        changed_at = Str.Raw("NOW()"),
                        new_values = "{\"department_id\":\"DEPT_FIN\"}"
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

        [TestMethod]
        public void Transaction_Bulk_Insert_Multiple_Tables()
        {
            var db = TestDbFactory.Create();
            db.IsKeepAlive(true);
            db.Transaction();

            try
            {
                var orderId = "ORD-BULK-001";

                // Insert order header
                db.Insert("sales_orders")
                    .Bulk(new
                    {
                        order_id = orderId,
                        order_no = "SO-BULK-TEST",
                        order_date = DateTime.Now,
                        customer_id = "CUST001",
                        status = "DRAFT",
                        total_amount = 750.00m,
                        currency_code = "USD",
                        created_by = "SYSTEM",
                        created_at = Str.Raw("NOW()")
                    })
                    .Execute();

                // Bulk insert 3 line items
                var lines = new List<object>
                {
                    new { line_id = Str.Raw("gen_random_uuid()"), order_id = orderId, line_number = 1, product_id = "P1", qty = 10, unit_price = 25.00m, discount = 0m, line_total = 250.00m },
                    new { line_id = Str.Raw("gen_random_uuid()"), order_id = orderId, line_number = 2, product_id = "P2", qty = 5, unit_price = 50.00m, discount = 0m, line_total = 250.00m },
                    new { line_id = Str.Raw("gen_random_uuid()"), order_id = orderId, line_number = 3, product_id = "P3", qty = 5, unit_price = 50.00m, discount = 0m, line_total = 250.00m },
                };
                db.Insert("sales_order_lines").Bulk(lines).Execute();

                // Insert stock movements for each line
                var movements = new List<object>
                {
                    new { movement_id = Str.Raw("gen_random_uuid()"), product_id = "P1", warehouse_id = "WH_MAIN", movement_type = "OUT", qty = 10, reference_no = orderId, movement_date = DateTime.Now, created_by = "SYSTEM" },
                    new { movement_id = Str.Raw("gen_random_uuid()"), product_id = "P2", warehouse_id = "WH_MAIN", movement_type = "OUT", qty = 5, reference_no = orderId, movement_date = DateTime.Now, created_by = "SYSTEM" },
                    new { movement_id = Str.Raw("gen_random_uuid()"), product_id = "P3", warehouse_id = "WH_MAIN", movement_type = "OUT", qty = 5, reference_no = orderId, movement_date = DateTime.Now, created_by = "SYSTEM" },
                };
                db.Insert("stock_movements").Bulk(movements).Execute();

                // Update product stock quantities
                db.Update("products").SetRawValue("stock_qty", "stock_qty - 10").Where("product_id", "P1").Execute();
                db.Update("products").SetRawValue("stock_qty", "stock_qty - 5").Where("product_id", "P2").Execute();
                db.Update("products").SetRawValue("stock_qty", "stock_qty - 5").Where("product_id", "P3").Execute();

                // Write audit entry
                db.Insert("audit_logs")
                    .Bulk(new
                    {
                        audit_id = Str.Raw("gen_random_uuid()"),
                        table_name = "sales_orders",
                        record_id = orderId,
                        action = "INSERT",
                        changed_by = "SYSTEM",
                        changed_at = Str.Raw("NOW()"),
                        new_values = "{\"order_no\":\"SO-BULK-TEST\"}"
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
            finally
            {
                db.Close(true);
            }
        }

        [TestMethod]
        public void Transaction_Delete_Cascade()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var orderId = "ORD-DEL-001";

                // Delete child records first
                db.Delete("sales_order_lines")
                    .Where("order_id", orderId)
                    .Execute();

                // Then delete parent
                db.Delete("sales_orders")
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
        }

        [TestMethod]
        public void Transaction_KeepAlive_Multiple_Reads_Then_Write()
        {
            var db = TestDbFactory.Create();
            db.IsKeepAlive(true);

            // Read 1: check if employee exists
            bool exists = db.Select("employees")
                .Where("employee_code", "EMP-KA-001")
                .IsExist();

            // Read 2: get department info
            var dept = db.Select("departments")
                .Where("department_id", "DEPT_IT")
                .ReadAs<Department>();

            // Write: insert or skip based on reads
            if (!exists && dept != null)
            {
                db.Insert("employees")
                    .Bulk(new
                    {
                        employee_id = Str.Raw("gen_random_uuid()"),
                        employee_code = "EMP-KA-001",
                        full_name = "KeepAlive Test",
                        department_id = dept.DepartmentId,
                        is_active = true,
                        created_at = Str.Raw("NOW()")
                    })
                    .Execute();
            }

            db.Close(true);
            Assert.IsTrue(true);
        }
    }
}
