using System.ComponentModel.DataAnnotations.Schema;

namespace TestDemo.Models
{
    /// <summary>
    /// Sales / purchase order header — ERP core.
    /// </summary>
    public class SalesOrder
    {
        [Column("order_id")]
        public string OrderId { get; set; }

        [Column("order_no")]
        public string OrderNo { get; set; }

        [Column("order_date")]
        public DateTime OrderDate { get; set; }

        [Column("customer_id")]
        public string CustomerId { get; set; }

        [Column("status")]
        public string Status { get; set; }

        [Column("total_amount")]
        public decimal TotalAmount { get; set; }

        [Column("currency_code")]
        public string CurrencyCode { get; set; }

        [Column("notes")]
        public string Notes { get; set; }

        [Column("created_by")]
        public string CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("approved_by")]
        public string ApprovedBy { get; set; }

        [Column("approved_at")]
        public DateTime? ApprovedAt { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// Line items for a sales order.
    /// </summary>
    public class SalesOrderLine
    {
        [Column("line_id")]
        public string LineId { get; set; }

        [Column("order_id")]
        public string OrderId { get; set; }

        [Column("line_number")]
        public int LineNumber { get; set; }

        [Column("product_id")]
        public string ProductId { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; }

        [Column("qty")]
        public int Qty { get; set; }

        [Column("unit_price")]
        public decimal UnitPrice { get; set; }

        [Column("discount")]
        public decimal Discount { get; set; }

        [Column("line_total")]
        public decimal LineTotal { get; set; }
    }

    /// <summary>
    /// Customer / business partner.
    /// </summary>
    public class Customer
    {
        [Column("customer_id")]
        public string CustomerId { get; set; }

        [Column("customer_name")]
        public string CustomerName { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("phone")]
        public string Phone { get; set; }

        [Column("city")]
        public string City { get; set; }

        [Column("country")]
        public string Country { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }
    }
}
