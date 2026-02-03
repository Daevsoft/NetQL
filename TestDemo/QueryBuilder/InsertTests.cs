using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class InsertTests
    {
        [TestMethod]
        public void Insert_UserProfileCategory_Should_Succeed()
        {
            var netql = TestDbFactory.Create();

            netql.Insert("UserProfileCategory")
                .Bulk(new
                {
                    UserId = "TEST_USER",
                    CategoryCode = "TEST_CAT",
                    ProfileName = "TEST_PROFILE"
                })
                .Execute();

            Assert.IsTrue(true);
        }
    }

}
