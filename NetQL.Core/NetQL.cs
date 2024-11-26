using netQL.Lib;
using System;
using System.Data;

namespace netQL.Core
{
    public class NetQL : DbUtils
    {
        public NetQL(IDbConnection connection) : base(connection)
        {
        }
        public NetQL(IDbConnection connection, Provider provider) : base(connection, provider)
        {
        }
        public NetQL(IDbConnection connection, char quotSql, char bindSymbol = '@') : base(connection, quotSql, bindSymbol)
        {
        }
    }
}
