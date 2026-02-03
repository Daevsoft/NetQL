using Daevsoft;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDemo.Infrastructure
{
    internal class TestDbFactory
    {
        public static NetQL Create()
        {
            var config = TestConfiguration.Load();
            var connStr = config.GetConnectionString("Default");

            Assert.IsFalse(string.IsNullOrEmpty(connStr));

            var conn = new NpgsqlConnection(connStr);
            return new NetQL(conn);
        }
    }
}
