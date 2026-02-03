using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class ExistsTests
    {
        [TestMethod]
        public void Film_Should_Exist_By_Title()
        {
            var netql = TestDbFactory.Create();

            var exists = netql
                .Select("md_film")
                .Where(new { film_title = "ONE PUNCH MAN" })
                .IsExist();

            Assert.IsTrue(exists);
        }
    }

}
