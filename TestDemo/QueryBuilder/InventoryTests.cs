using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for inventory & product management — Inventory module.
    /// Covers: product CRUD, stock queries, stock movement recording,
    /// low-stock alerts, and warehouse-level inventory.
    /// </summary>
    [TestClass]
    public class InventoryTests
    {
        // ─── PRODUCT QUERIES ───────────────────────────────────────────────

        [TestMethod]
        public void Select_Active_Products_With_Category()
        {
            var db = TestDbFactory.Create();

            var products = db.Select(
                    new string[] { "p.*", "c.category_name" },
                    "products p"
                )
                .LeftJoin("product_categories c", "p.category_id", "c.category_id")
                .Where("p.is_active", true)
                .AndNull("p.deleted_at")
                .Asc("p.product_name")
                .ReadAsList<Product>();

            Assert.IsNotNull(products);
        }

        [TestMethod]
        public void Select_Products_By_Category_Hierarchy()
        {
            var db = TestDbFactory.Create();

            // Find products in a category or any of its sub-categories
            var products = db.Select("products")
                .WhereIn("category_id", sub => sub
                    .Select("category_id", "product_categories")
                    .Where("parent_id", "CAT_ELECTRONICS"))
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAsList<Product>();

            Assert.IsNotNull(products);
        }

        [TestMethod]
        public void Select_Products_Low_Stock_Alert()
        {
            var db = TestDbFactory.Create();

            // Products where current stock is at or below reorder level
            var lowStock = db.Select("products")
                .WhereRaw(Str.Raw("stock_qty"), "<=", Str.Raw("reorder_level"))
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Asc("stock_qty")
                .ReadAsList<Product>();

            Assert.IsNotNull(lowStock);
        }

        [TestMethod]
        public void Select_Product_Search_By_Name_Or_Sku()
        {
            var db = TestDbFactory.Create();

            var keyword = "laptop";

            var products = db.Select("products")
                .Wrap(g => g
                    .WhereRaw(Str.Raw("LOWER(product_name)"), "LIKE", $"%{keyword.ToLower()}%")
                    .OrWhereRaw(Str.Raw("LOWER(sku)"), "LIKE", $"%{keyword.ToLower()}%")
                )
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Limit(20)
                .ReadAsList<Product>();

            Assert.IsNotNull(products);
        }

        // ─── STOCK MOVEMENT ────────────────────────────────────────────────

        [TestMethod]
        public void Insert_Stock_Inbound_Movement()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                // Record the movement
                db.Insert("stock_movements")
                    .Bulk(new
                    {
                        movement_id = Str.Raw("gen_random_uuid()"),
                        product_id = "PROD001",
                        warehouse_id = "WH_MAIN",
                        movement_type = "IN",
                        qty = 100,
                        reference_no = "PO-2025-050",
                        movement_date = DateTime.Now,
                        created_by = "WAREHOUSE_USER"
                    })
                    .Execute();

                // Update the product stock
                db.Update("products")
                    .SetRawValue("stock_qty", "stock_qty + 100")
                    .Where("product_id", "PROD001")
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
        public void Insert_Stock_Outbound_Movement()
        {
            var db = TestDbFactory.Create();
            db.Transaction();

            try
            {
                var productId = "PROD001";
                var qtyOut = 25;

                // Record the outbound movement
                db.Insert("stock_movements")
                    .Bulk(new
                    {
                        movement_id = Str.Raw("gen_random_uuid()"),
                        product_id = productId,
                        warehouse_id = "WH_MAIN",
                        movement_type = "OUT",
                        qty = qtyOut,
                        reference_no = "SO-2025-0001",
                        movement_date = DateTime.Now,
                        created_by = "SALES_USER"
                    })
                    .Execute();

                // Decrease the product stock
                db.Update("products")
                    .SetRawValue("stock_qty", $"stock_qty - {qtyOut}")
                    .Where("product_id", productId)
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
        public void Select_Stock_Movement_History()
        {
            var db = TestDbFactory.Create();

            var movements = db.Select(
                    new string[] { "sm.*", "p.product_name", "p.sku" },
                    "stock_movements sm"
                )
                .Join("products p", "sm.product_id", "p.product_id")
                .Where("sm.product_id", "PROD001")
                .Where("sm.movement_date", ">=", DateTime.Now.AddMonths(-3))
                .Desc("sm.movement_date")
                .ReadAsList<StockMovement>();

            Assert.IsNotNull(movements);
        }

        // ─── INVENTORY REPORTING ───────────────────────────────────────────

        [TestMethod]
        public void Select_Stock_Summary_By_Warehouse()
        {
            var db = TestDbFactory.Create();

            var summary = db.Select(
                    Str.Raw("warehouse_id as label, COUNT(DISTINCT product_id) as total_count, SUM(qty) as total_amount"),
                    "stock_movements"
                )
                .Where("movement_type", "IN")
                .GroupBy("warehouse_id")
                .Desc(Str.Raw("total_amount"))
                .ReadAsList<ReportRow>();

            Assert.IsNotNull(summary);
        }

        [TestMethod]
        public void Select_Products_Never_Sold()
        {
            var db = TestDbFactory.Create();

            // Products that have no outbound stock movements
            var unsold = db.Select("products")
                .WhereNotIn("product_id", sub => sub
                    .Select("product_id", "stock_movements")
                    .Where("movement_type", "OUT"))
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAsList<Product>();

            Assert.IsNotNull(unsold);
        }
    }
}
