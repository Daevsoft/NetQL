using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo
{
    /// <summary>
    /// CRUD tests for the me_config and UserProfileCategory tables.
    /// Validates NetQL query-builder operations against a live database.
    /// </summary>
    [TestClass]
    public sealed class Test1
    {
        // ─── SELECT ────────────────────────────────────────────────────────

        [TestMethod]
        public void Select_MeConfig_By_Code()
        {
            var db = TestDbFactory.Create();

            var config = db.Select("me_config")
                .Where("config_code", "S0S")
                .ReadAs<MeConfig>();

            Assert.IsNotNull(config);
        }

        [TestMethod]
        public void Select_MeConfig_By_Category_With_Module_Filter()
        {
            var db = TestDbFactory.Create();

            var config = db.Select("me_config")
                .Where(new
                {
                    config_code = "S0S",
                    category = "GEN21 Digital Asset"
                })
                .Wrap(g => g
                    .Where(new { module = "Services" })
                    .OrWhere(new { module = "Audit Trail" })
                )
                .ReadAs<MeConfig>();

            Assert.IsNotNull(config);
        }

        [TestMethod]
        public void Select_MeConfig_List_By_Category()
        {
            var db = TestDbFactory.Create();

            var configs = db.Select("me_config")
                .Where("category", "GEN21 Digital Asset")
                .ReadAsList<MeConfig>();

            Assert.IsNotNull(configs);
        }

        [TestMethod]
        public void Select_UserProfileCategory_With_Composite_Where()
        {
            var db = TestDbFactory.Create();

            var profiles = db.Select("UserProfileCategory")
                .Where("UserId", "opopop")
                .Where("CategoryCode", "Test4")
                .ReadAsList<UserProfileCategory>();

            Assert.IsNotNull(profiles);
        }

        [TestMethod]
        public void Select_MeConfig_IsExist()
        {
            var db = TestDbFactory.Create();

            bool exists = db.Select("me_config")
                .Where(new
                {
                    config_code = "S0S",
                    category = "GEN21 Digital Asset"
                })
                .IsExist();

            // Verify the query runs without exception
            Assert.IsTrue(true);
        }

        // ─── INSERT ────────────────────────────────────────────────────────

        [TestMethod]
        public void Insert_UserProfileCategory()
        {
            var db = TestDbFactory.Create();

            db.Insert("UserProfileCategory")
                .Bulk(new
                {
                    UserId = "TEST_USER_001",
                    CategoryCode = "TEST_CAT",
                    ProfileName = "Test Profile",
                })
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Insert_UserProfileCategory_With_Null_ProfileName()
        {
            var db = TestDbFactory.Create();

            db.Insert("UserProfileCategory")
                .Bulk(new
                {
                    UserId = "TEST_USER_002",
                    CategoryCode = "TEST_CAT_NULL",
                    ProfileName = (string)null,
                })
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── UPDATE ────────────────────────────────────────────────────────

        [TestMethod]
        public void Update_UserProfileCategory_By_UserId()
        {
            var db = TestDbFactory.Create();

            db.Update("UserProfileCategory")
                .Bulk(new
                {
                    CategoryCode = "UPDATED_CAT",
                    ProfileName = "Updated Profile",
                })
                .Where("UserId", "TEST_USER_001")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Update_UserProfileCategory_With_Composite_Where()
        {
            var db = TestDbFactory.Create();

            db.Update("UserProfileCategory")
                .Bulk(new
                {
                    ProfileName = "Renamed Profile",
                })
                .Where("UserId", "TEST_USER_001")
                .Where("CategoryCode", "UPDATED_CAT")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── DELETE ────────────────────────────────────────────────────────

        [TestMethod]
        public void Delete_UserProfileCategory_By_UserId()
        {
            var db = TestDbFactory.Create();

            db.Delete("UserProfileCategory")
                .Where("UserId", "TEST_USER_001")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Delete_UserProfileCategory_With_Composite_Where()
        {
            var db = TestDbFactory.Create();

            db.Delete("UserProfileCategory")
                .Where("UserId", "TEST_USER_002")
                .Where("CategoryCode", "TEST_CAT_NULL")
                .Execute();

            Assert.IsTrue(true);
        }
    }
}
