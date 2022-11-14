using netQL.Lib;

namespace netQL
{
    public class NetQL<T>: DbUtils<T>
    {
        public NetQL(T connection, Provider provider):base(connection, provider)
        {
        }
    }
}