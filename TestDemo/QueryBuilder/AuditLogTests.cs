using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for audit trail and activity logging — compliance & governance.
    /// Covers: insert audit entries, query audit history, filter by action/date/user,
    /// and audit for data governance scenarios.
    /// </summary>
    [TestClass]
    public class AuditLogTests
    {
        [TestMethod]
        public void Insert_Audit_Log_Entry()
        {
            var db = TestDbFactory.Create();

            db.Insert("audit_logs")
                .Bulk(new
                {
                    audit_id = Str.Raw("gen_random_uuid()"),
                    table_name = "employees",
                    record_id = "EMP001",
                    action = "UPDATE",
                    changed_by = "ADMIN001",
                    changed_at = Str.Raw("NOW()"),
                    old_values = "{\"department_id\":\"DEPT_IT\"}",
                    new_values = "{\"department_id\":\"DEPT_FIN\"}"
                })
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Select_Audit_History_For_Record()
        {
            var db = TestDbFactory.Create();

            var history = db.Select("audit_logs")
                .Where("table_name", "employees")
                .Where("record_id", "EMP001")
                .Desc("changed_at")
                .ReadAsList<AuditLog>();

            Assert.IsNotNull(history);
        }

        [TestMethod]
        public void Select_Audit_By_User_And_DateRange()
        {
            var db = TestDbFactory.Create();

            var logs = db.Select("audit_logs")
                .Where("changed_by", "ADMIN001")
                .Where("changed_at", ">=", DateTime.Now.AddDays(-7))
                .Desc("changed_at")
                .Limit(100)
                .ReadAsList<AuditLog>();

            Assert.IsNotNull(logs);
        }

        [TestMethod]
        public void Select_Audit_By_Action_Type()
        {
            var db = TestDbFactory.Create();

            var deletes = db.Select("audit_logs")
                .Where("action", "DELETE")
                .Where("changed_at", ">=", DateTime.Now.AddDays(-30))
                .Desc("changed_at")
                .ReadAsList<AuditLog>();

            Assert.IsNotNull(deletes);
        }

        [TestMethod]
        public void Select_Audit_Across_Multiple_Tables()
        {
            var db = TestDbFactory.Create();

            // All changes across orders and employees this week
            var logs = db.Select("audit_logs")
                .WhereIn("table_name", new string[] { "sales_orders", "employees" })
                .Where("changed_at", ">=", DateTime.Now.AddDays(-7))
                .Desc("changed_at")
                .Limit(200)
                .ReadAsList<AuditLog>();

            Assert.IsNotNull(logs);
        }

        [TestMethod]
        public void Select_Audit_Count_By_Table_Summary()
        {
            var db = TestDbFactory.Create();

            var summary = db.Select(
                    Str.Raw("table_name as label, COUNT(*) as total_count, 0 as total_amount"),
                    "audit_logs"
                )
                .Where("changed_at", ">=", DateTime.Now.AddMonths(-1))
                .GroupBy("table_name")
                .Desc(Str.Raw("total_count"))
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(summary);
        }

        [TestMethod]
        public void Transactional_Update_With_Audit_Trail()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var orderId = "ORD-AUDIT-001";
                var userId = "MGR001";

                // Update the order
                db.Update("sales_orders")
                    .SetValue("status", "APPROVED")
                    .SetValue("approved_by", userId)
                    .SetValue("approved_at", Str.Raw("NOW()"))
                    .Where("order_id", orderId)
                    .Where("status", "PENDING")
                    .Execute();

                // Log the approval
                db.Insert("audit_logs")
                    .Bulk(new
                    {
                        audit_id = Str.Raw("gen_random_uuid()"),
                        table_name = "sales_orders",
                        record_id = orderId,
                        action = "APPROVE",
                        changed_by = userId,
                        changed_at = Str.Raw("NOW()"),
                        old_values = "{\"status\":\"PENDING\"}",
                        new_values = "{\"status\":\"APPROVED\"}"
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
    }
}
