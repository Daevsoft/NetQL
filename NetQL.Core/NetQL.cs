using netQL.Lib;
using System;

namespace NetQL.Core
{
    public class NetQL<T> : DbUtils<T>
    {
        public NetQL(T connection) : base(connection)
        {
        }
        public NetQL(T connection, Provider provider) : base(connection, provider)
        {
        }
        public NetQL(T connection, char quotSql, char bindSymbol = '@') : base(connection, quotSql, bindSymbol)
        {
        }
    }
}
