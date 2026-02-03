using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class SelectTests
    {
        [TestMethod]
        public void Select_UserProfileCategory_By_UserId_And_Category()
        {
            var netql = TestDbFactory.Create();

            var result = netql
                .Select("UserProfileCategory")
                .Where("UserId", "opopop")
                .Where("CategoryCode", "Test4")
                .ReadAsList<UserProfileCategory>();

            Assert.IsNotNull(result);
        }
    }

}
