using Daevsoft.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    [TestClass]
    public class SelectRawTests
    {
        [TestMethod]
        public void Select_Count_With_StrRaw_Should_Work()
        {
            var netql = TestDbFactory.Create();

            // Act
            var exists = netql
                .Select(Str.Raw("count(*) as _count"), "trx_qc_status_hdr")
                .Where("material_id", "TEST_MATERIAL_ID")
                .IsExist();

            // Assert
            Assert.IsTrue(exists);
        }

        [TestMethod]
        public void Select_With_Join_And_Raw_Select_Should_Work()
        {
            var netql = TestDbFactory.Create();

            var result = netql
                .Select(Str.Raw("sme.row_id_epi, tqh.*"), "trx_qc_technical_hist tqh")
                .Join(
                    "trx_qc_status_hdr tqs",
                    "tqs.material_id=tqh.material_id AND tqs.revision_no=tqh.revision_no"
                )
                .Join(
                    "stock_material_epi sme",
                    "tqh.material_id",
                    "sme.material_id"
                )
                .Where("tqs.row_id", "MMMMMMMMMMM2009361")
                .Where("tqh.revision_no", 5)
                .Where("tqs.revision_no", 5)
                .ReadAsList<MediaQCTrans>();

            Assert.IsNotNull(result);
        }

    }

}
