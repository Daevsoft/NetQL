using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDemo.QueryBuilder
{
    using Daevsoft.Lib;
    using System.IO;
    using TestDemo.Infrastructure;
    using TestDemo.Utils;

    [TestClass]
    public class InsertBulkTests
    {
        [TestMethod]
        public void Insert_Bulk_MamMediaFile_Should_Succeed()
        {
            var netql = TestDbFactory.Create();

            // Arrange
            var filename =
                @"\\MY_PC_SHARE\HD_2025120500_00.mp4";

            var newMamMediaId = Path.GetFileNameWithoutExtension(filename);
            var extension = Path.GetExtension(filename).TrimStart('.').ToUpper();
            var pathDir = Path.GetDirectoryName(filename);
            var mediaFilename = Path.GetFileName(filename);
            var mamDirectoryId = "MMMMMMMMMMM2724273";

            // RAW function (same row_id for both)
            var rowIdRaw = Str.Raw("get_row_id()");

            var bulkData = new List<object>
        {
            new
            {
                mam_media_id = newMamMediaId,
                media_type = "LOW",
                file_type = extension,
                file_name = mediaFilename,
                file_dir = pathDir,
                token = Randomize.GenerateRandom(1, 10000000, 99999999)[0].ToString(),
                vidchecker_id = 0,
                row_id_mam_directory = mamDirectoryId,
                row_id = rowIdRaw
            },
            new
            {
                mam_media_id = newMamMediaId,
                media_type = "VID",
                file_type = extension,
                file_name = mediaFilename,
                file_dir = pathDir,
                token = Randomize.GenerateRandom(1, 10000000, 99999999)[0].ToString(),
                vidchecker_id = 0,
                row_id_mam_directory = mamDirectoryId,
                row_id = rowIdRaw
            }
        };

            // Act
            netql.Insert("mam_media_file")
                 .Bulk(bulkData)
                 .Execute();

            netql.Close(true);

            // Assert
            Assert.IsTrue(true);
        }
    }

}
