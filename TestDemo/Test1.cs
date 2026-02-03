using Daevsoft;
using Daevsoft.Lib;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.ComponentModel.DataAnnotations.Schema;

namespace TestDemo
{
    
    
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var config = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json")
                            .AddJsonFile("appsettings.Testing.json", optional: true)
                            .AddEnvironmentVariables()
                            .Build();
            string connectionStr = config.GetConnectionString("Default");
            Assert.IsNotEmpty(connectionStr);
            return;
            NpgsqlConnection dbConnection = new NpgsqlConnection(connectionStr);
            NetQL netql = new NetQL(dbConnection);
            //netql.Select("job_directory_folder").ReadAsList<dynamic>();
            //netql.Transaction();
            //netql.Update("UserProfileCategory")
            //    .Bulk(new
            //    {
            //        CategoryCode = "Test5",
            //        ProfileName= "Test5",
            //    })
            //    .Where("UserId", "abcd")
            //    .Execute();


            //netql.Insert("UserProfileCategory")
            //    .SetBulk(new
            //    {
            //        UserId = "opopop",
            //        CategoryCode = "Test4",
            //        ProfileName = "Test4",
            //    }).Execute();

            //netql.Select("UserProfileCategory")
            //    .Where(_db =>
            //    {
            //        return _db.Where("UserId", "opopop").Where("CategoryCode", "Test4");
            //    })
            //    .ReadAsList<UserProfileCategory>();

            //bool isExist = netql.Select("row_id", "md_film")
            //    .Wrap(_ =>
            //    {
            //        return _.OrWhere(new
            //        {
            //            film_title_org = "KRY PROGRAM JUL",
            //            film_title = "KRY PROGRAM JUL"
            //        });
            //    })
            //    .Wrap(_ =>
            //    {
            //        return _.OrWhere(new
            //        {
            //            season = "1",
            //            year_release = 2025
            //        });
            //    })
            //    .IsExist();

            //netql.Update("mam_media_file")
            //    .SetRawValue("file_dir", "REPLACE(file_dir, :sourceDirs, :targetDirs)")
            //    .WhereRaw(Str.Raw("0"), "<", "POSITION(:sourceDirs IN file_dir)")
            //    .Where("file_type", "LOW")
            //    .AddParameter("sourceDirs", "\\\\stor-mam.infotech\\store_mam\\DEV\\HiREss\\Material\\2025\\December\\Week 2")
            //    .AddParameter("targetDirs", "\\\\stor-mam.infotech\\store_mam\\DEV\\HiREss\\Material\\2025\\December\\WEEK 22")
            //    .Execute();
            netql.IsKeepAlive(true);

            var xSos = netql.Select("me_config")
                .Where(Str.Raw("'S0S'"), Str.Raw("config_code "))
                    .Where(new
                    {
                        category = "GEN21 Digital Asset"
                    }).Wrap((g) =>
                    {
                        return g.Where(new
                        {
                            module = "Services"
                        }).OrWhere(new
                        {
                            module = "Audit Trail"
                        });
                    }).ReadAs<MeConfig>();

            //netql.Insert("UserProfileCategory")
            //    .SetBulk(new
            //    {
            //        UserId = "RRRT",
            //        CategoryCode = "Test6",
            //        ProfileName = null,
            //    }).Execute();
            //            netql.Query("""
            //Insert into "UserProfileCategory" ("UserId", "CategoryCode", "ProfileName")
            //Values ('AADDD', 'Test7', null)
            //""").Execute();

            //MediaQCTrans r = netql.Select("sme.row_id_epi, tqh.*", "trx_qc_technical_hist tqh")
            //    .Join("stock_material_epi SME", "tqh.material_id", "sme.material_id")
            //    .Where("sme.row_id_epi", "MMMMMMMMMMM1700945")
            //    .Where("tqh.revision_no", 10, System.Data.DbType.VarNumeric)
            //    .ReadAs<MediaQCTrans>();
            //var MaterialId = "MM00004283";
            //netql.Transaction();
            //List<object> mediaFileReadyInsert = new List<object>();
            //string filename = "\\\\stor-mam.infotech\\store_mam\\QC\\mam_kompas\\copytayang\\KOMPASTV\\KOMPAS TV-HD\\20251205\\1_TS-1_KOMPAS TV-HD_2025120500_00.mp4";
            //string newMamMediaId = Path.GetFileNameWithoutExtension(filename);
            //string newRowId = Str.Raw("get_row_id()");
            //string extension = Path.GetExtension(filename).TrimStart('.').ToUpper();
            //string pathDir = Path.GetDirectoryName(filename);
            //string mediaFilename = Path.GetFileName(filename);
            //string mamDirectoryId = "MMMMMMMMMMM2724273";
            //// As LOW media and VID media
            //mediaFileReadyInsert.Add(new
            //{
            //    mam_media_id = newMamMediaId,
            //    media_type = "LOW",
            //    file_type = extension,
            //    file_name = mediaFilename,
            //    file_dir = pathDir,
            //    token = Randomize.GenerateRandom(1, 10000000, 99999999)[0].ToString(),
            //    vidchecker_id = 0,
            //    row_id_mam_directory = mamDirectoryId,
            //    row_id = newRowId
            //});
            //mediaFileReadyInsert.Add(new
            //{
            //    mam_media_id = newMamMediaId,
            //    media_type = "VID",
            //    file_type = extension,
            //    file_name = mediaFilename,
            //    file_dir = pathDir,
            //    token = Randomize.GenerateRandom(1, 10000000, 99999999)[0].ToString(),
            //    vidchecker_id = 0,
            //    row_id_mam_directory = mamDirectoryId,
            //    row_id = newRowId
            //});
            //// Change with positional parameter
            //netql.Insert("mam_media_file").Bulk(mediaFileReadyInsert).Execute();
            ////var a = netql.Select("sme.row_id_epi, tqh.*", "trx_qc_technical_hist tqh")
            ////                    .Join("trx_qc_status_hdr tqs", "tqs.material_id=tqh.material_id AND tqs.revision_no=tqh.revision_no")
            ////                    .Join("stock_material_epi SME", "tqh.material_id", "sme.material_id")
            ////                    .Where("tqs.row_id", "MMMMMMMMMMM2009361")
            ////                    .Where("tqh.revision_no", 5, DbType.VarNumeric)
            ////                    .Where("tqs.revision_no", 5, DbType.VarNumeric)
            ////                    .ReadAs<MediaQCTrans>();
            ////var checkExist = netql.Select(Str.Raw("count(*) as _count"), "trx_qc_status_hdr").Where("material_id", MaterialId).IsExist();
            ////netql.Commit();
            ///


            /* @"
            SELECT config_code, config_promt, config_param, category, module
            FROM me_config
            WHERE config_code = :configCode 
            AND category = 'GEN21 Digital Asset' 
            AND (module = 'Services' OR module = 'Audit Trail')";
            */
            //string configCode = "S0X";
            //MeConfig retval = netql.Select("me_config")
            //        .Where(new
            //        {
            //            config_code = configCode,
            //            category = "GEN21 Digital Asset"
            //        }).Wrap((g) =>
            //        {
            //            return g.Where(new
            //            {
            //                module = "Services"
            //            }).OrWhere(new
            //            {
            //                module = "Audit Trail"
            //            });
            //        }).ReadAs<MeConfig>();

            var isExist = netql.Select("md_film")
                            .Where(new
                            {
                                film_title = "ONE PUNCH MAN",
                            }).IsExist();
            netql.Close(true);
            Assert.IsTrue(true, "TestMethod1 passed");
        }
    }
    
}
