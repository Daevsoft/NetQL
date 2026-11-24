using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for approval workflow operations — enterprise document management.
    /// Covers: multi-step approval creation, status transitions, pending approval queries,
    /// approval history, and batch approval processing.
    /// </summary>
    [TestClass]
    public class ApprovalWorkflowTests
    {
        [TestMethod]
        public void Insert_Approval_Steps_For_New_Order()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var documentId = "ORD-APPR-001";

                // Create a 3-step approval chain
                var approvalSteps = new List<object>
                {
                    new
                    {
                        approval_id = Str.Raw("gen_random_uuid()"),
                        document_type = "SALES_ORDER",
                        document_id = documentId,
                        step_no = 1,
                        approver_id = "MGR_SALES",
                        status = "PENDING"
                    },
                    new
                    {
                        approval_id = Str.Raw("gen_random_uuid()"),
                        document_type = "SALES_ORDER",
                        document_id = documentId,
                        step_no = 2,
                        approver_id = "MGR_FINANCE",
                        status = "PENDING"
                    },
                    new
                    {
                        approval_id = Str.Raw("gen_random_uuid()"),
                        document_type = "SALES_ORDER",
                        document_id = documentId,
                        step_no = 3,
                        approver_id = "DIR_OPS",
                        status = "PENDING"
                    }
                };

                db.Insert("approval_workflows").Bulk(approvalSteps).Execute();

                // Update document status
                db.Update("sales_orders")
                    .SetValue("status", "PENDING_APPROVAL")
                    .Where("order_id", documentId)
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
        public void Select_Pending_Approvals_For_User()
        {
            var db = TestDbFactory.Create();

            var approverId = "MGR_SALES";

            var pending = db.Select(
                    new string[] {
                        "aw.*",
                        "o.order_no", "o.total_amount", "o.created_by"
                    },
                    "approval_workflows aw"
                )
                .Join("sales_orders o", "aw.document_id", "o.order_id")
                .Where("aw.approver_id", approverId)
                .Where("aw.status", "PENDING")
                .Where("aw.document_type", "SALES_ORDER")
                .Asc("aw.step_no")
                .ReadAsList<ApprovalWorkflow>();

            Assert.IsNotNull(pending);
        }

        [TestMethod]
        public void Update_Approve_Step()
        {
            var db = TestDbFactory.Create();

            db.Update("approval_workflows")
                .SetValue("status", "APPROVED")
                .SetValue("comments", "Looks good. Approved.")
                .SetValue("action_date", Str.Raw("NOW()"))
                .Where("document_id", "ORD-APPR-001")
                .Where("step_no", 1)
                .Where("status", "PENDING")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Update_Reject_Step_With_Comment()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var documentId = "ORD-APPR-001";

                // Reject the current step
                db.Update("approval_workflows")
                    .SetValue("status", "REJECTED")
                    .SetValue("comments", "Budget exceeded. Please revise.")
                    .SetValue("action_date", Str.Raw("NOW()"))
                    .Where("document_id", documentId)
                    .Where("step_no", 2)
                    .Where("status", "PENDING")
                    .Execute();

                // Update the document status back to DRAFT
                db.Update("sales_orders")
                    .SetValue("status", "DRAFT")
                    .Where("order_id", documentId)
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
        public void Select_Approval_History_For_Document()
        {
            var db = TestDbFactory.Create();

            var history = db.Select(
                    new string[] { "aw.*", "u.full_name" },
                    "approval_workflows aw"
                )
                .LeftJoin("users u", "aw.approver_id", "u.user_id")
                .Where("aw.document_id", "ORD-APPR-001")
                .Asc("aw.step_no")
                .ReadAsList<ApprovalWorkflow>();

            Assert.IsNotNull(history);
        }

        [TestMethod]
        public void Check_All_Steps_Approved()
        {
            var db = TestDbFactory.Create();

            // If any step is still PENDING, the document is not fully approved
            bool hasPending = db.Select("approval_workflows")
                .Where("document_id", "ORD-APPR-001")
                .Where("status", "PENDING")
                .IsExist();

            // hasPending == false means all steps are approved
            Assert.IsFalse(hasPending, "Expected all steps to be approved");
        }

        [TestMethod]
        public void Select_Documents_Fully_Approved()
        {
            var db = TestDbFactory.Create();

            // Orders that have no remaining PENDING approval steps
            var fullyApproved = db.Select("sales_orders")
                .WhereNotIn("order_id", sub => sub
                    .Select("document_id", "approval_workflows")
                    .Where("status", "PENDING")
                    .Where("document_type", "SALES_ORDER"))
                .Where("status", "PENDING_APPROVAL")
                .AndNull("deleted_at")
                .ReadAsList<SalesOrder>();

            Assert.IsNotNull(fullyApproved);
        }

        [TestMethod]
        public void Batch_Approve_All_Pending_For_User()
        {
            var db = TestDbFactory.Create();

            db.Update("approval_workflows")
                .SetValue("status", "APPROVED")
                .SetValue("comments", "Batch approved")
                .SetValue("action_date", Str.Raw("NOW()"))
                .Where("approver_id", "MGR_SALES")
                .Where("status", "PENDING")
                .Where("document_type", "SALES_ORDER")
                .Execute();

            Assert.IsTrue(true);
        }
    }
}
