using System.ComponentModel.DataAnnotations.Schema;

namespace TestDemo.Models
{
    /// <summary>
    /// Audit trail entry — tracks all data changes for compliance.
    /// </summary>
    public class AuditLog
    {
        [Column("audit_id")]
        public string AuditId { get; set; }

        [Column("table_name")]
        public string TableName { get; set; }

        [Column("record_id")]
        public string RecordId { get; set; }

        [Column("action")]
        public string Action { get; set; } // INSERT, UPDATE, DELETE

        [Column("changed_by")]
        public string ChangedBy { get; set; }

        [Column("changed_at")]
        public DateTime ChangedAt { get; set; }

        [Column("old_values")]
        public string OldValues { get; set; }

        [Column("new_values")]
        public string NewValues { get; set; }
    }

    /// <summary>
    /// Application configuration — key/value store per module.
    /// </summary>
    public class AppConfig
    {
        [Column("config_id")]
        public string ConfigId { get; set; }

        [Column("config_code")]
        public string ConfigCode { get; set; }

        [Column("config_value")]
        public string ConfigValue { get; set; }

        [Column("category")]
        public string Category { get; set; }

        [Column("module")]
        public string Module { get; set; }

        [Column("description")]
        public string Description { get; set; }
    }

    /// <summary>
    /// User subscription with expiration — SaaS / licensing.
    /// </summary>
    public class UserSubscription
    {
        [Column("subscription_id")]
        public string SubscriptionId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("plan_code")]
        public string PlanCode { get; set; }

        [Column("status")]
        public string Status { get; set; } // ACTIVE, EXPIRED, CANCELLED

        [Column("start_date")]
        public DateTime StartDate { get; set; }

        [Column("expired_date")]
        public DateTime ExpiredDate { get; set; }

        [Column("email_sent")]
        public bool EmailSent { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// User account — authentication / authorization.
    /// </summary>
    public class UserAccount
    {
        [Column("user_id")]
        public string UserId { get; set; }

        [Column("username")]
        public string Username { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("role")]
        public string Role { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// Approval workflow step.
    /// </summary>
    public class ApprovalWorkflow
    {
        [Column("approval_id")]
        public string ApprovalId { get; set; }

        [Column("document_type")]
        public string DocumentType { get; set; }

        [Column("document_id")]
        public string DocumentId { get; set; }

        [Column("step_no")]
        public int StepNo { get; set; }

        [Column("approver_id")]
        public string ApproverId { get; set; }

        [Column("status")]
        public string Status { get; set; } // PENDING, APPROVED, REJECTED

        [Column("comments")]
        public string Comments { get; set; }

        [Column("action_date")]
        public DateTime? ActionDate { get; set; }
    }

    /// <summary>
    /// Aggregation result for reporting queries.
    /// </summary>
    public class ReportRow
    {
        [Column("label")]
        public string Label { get; set; }

        [Column("total_count")]
        public int TotalCount { get; set; }

        [Column("total_amount")]
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// Simple count result for scalar queries.
    /// </summary>
    public class CountResult
    {
        [Column("total")]
        public int Total { get; set; }
    }
}
