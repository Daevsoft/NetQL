using System.ComponentModel.DataAnnotations.Schema;

namespace TestDemo.Models
{
    /// <summary>
    /// Product / inventory item — Inventory module.
    /// </summary>
    public class Product
    {
        [Column("product_id")]
        public string ProductId { get; set; }

        [Column("sku")]
        public string Sku { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; }

        [Column("category_id")]
        public string CategoryId { get; set; }

        [Column("unit_price")]
        public decimal UnitPrice { get; set; }

        [Column("stock_qty")]
        public int StockQty { get; set; }

        [Column("reorder_level")]
        public int ReorderLevel { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// Product category hierarchy.
    /// </summary>
    public class ProductCategory
    {
        [Column("category_id")]
        public string CategoryId { get; set; }

        [Column("category_name")]
        public string CategoryName { get; set; }

        [Column("parent_id")]
        public string ParentId { get; set; }
    }

    /// <summary>
    /// Stock movement / inventory transaction.
    /// </summary>
    public class StockMovement
    {
        [Column("movement_id")]
        public string MovementId { get; set; }

        [Column("product_id")]
        public string ProductId { get; set; }

        [Column("warehouse_id")]
        public string WarehouseId { get; set; }

        [Column("movement_type")]
        public string MovementType { get; set; } // IN, OUT, ADJUST

        [Column("qty")]
        public int Qty { get; set; }

        [Column("reference_no")]
        public string ReferenceNo { get; set; }

        [Column("movement_date")]
        public DateTime MovementDate { get; set; }

        [Column("created_by")]
        public string CreatedBy { get; set; }
    }
}
