using Daevsoft.Lib;
using System;
using System.Data;

namespace Daevsoft.Core
{
    public class NetQL : DbUtils
    {
        public NetQL(IDbConnection connection) : base(connection)
        {
        }
        public NetQL(string connectionString, Provider provider) : base(connectionString, provider)
        {
        }
        public NetQL(IDbConnection connection, Provider provider) : base(connection, provider)
        {
        }
        public NetQL(IDbConnection connection, char quotSql, char bindSymbol = '@') : base(connection, quotSql, bindSymbol)
        {
        }
        public NetQL(IDbConnection connection, char quotSql, char bindSymbol = '@') : base(connection, quotSql, bindSymbol)
        {
        }

    }
}
