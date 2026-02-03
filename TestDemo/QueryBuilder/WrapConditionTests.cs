using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class WrapConditionTests
    {
        [TestMethod]
        public void Wrap_OrWhere_Should_Work()
        {
            var netql = TestDbFactory.Create();

            var result = netql.Select("me_config")
                .Where(new { category = "Digital Asset" })
                .Wrap(g =>
                    g.Where(new { module = "Services" })
                     .OrWhere(new { module = "Audit Trail" })
                )
                .ReadAsList<MeConfig>();

            Assert.IsNotNull(result);
        }
    }

}
