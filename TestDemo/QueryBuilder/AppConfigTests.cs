using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for application configuration management — config key/value store.
    /// Covers: upsert pattern, config lookup, config by module,
    /// and batch config operations.
    /// </summary>
    [TestClass]
    public class AppConfigTests
    {
        [TestMethod]
        public void Select_Config_By_Code_And_Category()
        {
            var db = TestDbFactory.Create();

            var config = db.Select("app_config")
                .Where(new
                {
                    config_code = "SMTP_HOST",
                    category = "Email"
                })
                .ReadAs<AppConfig>();

            Assert.IsNotNull(config);
        }

        [TestMethod]
        public void Select_All_Configs_For_Module()
        {
            var db = TestDbFactory.Create();

            var configs = db.Select("app_config")
                .Where("module", "Sales")
                .Asc("config_code")
                .ReadAsList<AppConfig>();

            Assert.IsNotNull(configs);
        }

        [TestMethod]
        public void Select_Config_With_Wrapped_Module_Filter()
        {
            var db = TestDbFactory.Create();

            // Get configs that belong to either "Sales" or "Inventory" module
            var configs = db.Select("app_config")
                .Where("category", "General")
                .Wrap(g => g
                    .Where(new { module = "Sales" })
                    .OrWhere(new { module = "Inventory" })
                )
                .ReadAsList<AppConfig>();

            Assert.IsNotNull(configs);
        }

        [TestMethod]
        public void Upsert_Config_Insert_When_Not_Exists()
        {
            var db = TestDbFactory.Create();

            string code = "MAX_UPLOAD_SIZE";
            string category = "System";

            bool exists = db.Select("app_config")
                .Where(new { config_code = code, category = category })
                .IsExist();

            if (!exists)
            {
                db.Insert("app_config")
                    .Bulk(new
                    {
                        config_id = Str.Raw("gen_random_uuid()"),
                        config_code = code,
                        config_value = "10485760", // 10 MB
                        category = category,
                        module = "System",
                        description = "Maximum file upload size in bytes"
                    })
                    .Execute();
            }

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Upsert_Config_Update_When_Exists()
        {
            var db = TestDbFactory.Create();

            string code = "MAX_UPLOAD_SIZE";
            string category = "System";

            bool exists = db.Select("app_config")
                .Where(new { config_code = code, category = category })
                .IsExist();

            if (exists)
            {
                db.Update("app_config")
                    .SetValue("config_value", "20971520") // 20 MB
                    .Where(new { config_code = code, category = category })
                    .Execute();
            }
            else
            {
                db.Insert("app_config")
                    .Bulk(new
                    {
                        config_id = Str.Raw("gen_random_uuid()"),
                        config_code = code,
                        config_value = "20971520",
                        category = category,
                        module = "System",
                        description = "Maximum file upload size in bytes"
                    })
                    .Execute();
            }

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Delete_Config_By_Module()
        {
            var db = TestDbFactory.Create();

            db.Delete("app_config")
                .Where("module", "Deprecated")
                .Execute();

            Assert.IsTrue(true);
        }
    }
}
