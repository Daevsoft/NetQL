using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDemo.Utils
{
    [TestClass]
    public class RandomizeTests
    {
        [TestMethod]
        public void GenerateRandom_Should_Return_Unique_Values()
        {
            var result = Randomize.GenerateRandom(5, 1, 10);

            Assert.AreEqual(5, result.Distinct().Count());
        }
    }

}
