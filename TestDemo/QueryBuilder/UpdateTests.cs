using Daevsoft.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class UpdateTests
    {
        [TestMethod]
        public void Update_File_Directory_With_Raw_SQL()
        {
            var netql = TestDbFactory.Create();

            netql.Update("mam_media_file")
                .SetRawValue("file_dir", "REPLACE(file_dir, :src, :dst)")
                .WhereRaw(Str.Raw("0"), "<", "POSITION(:src IN file_dir)")
                .AddParameter("src", "OLD_PATH")
                .AddParameter("dst", "NEW_PATH")
                .Execute();

            Assert.IsTrue(true);
        }
    }

}
