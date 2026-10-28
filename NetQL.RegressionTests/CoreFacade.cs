#if CORE_VARIANT
using System.Data;
using Daevsoft.Lib;

namespace Daevsoft;

public sealed class NetQL : Core.NetQL
{
    public NetQL(IDbConnection connection) : base(connection) { }
    public NetQL(IDbConnection connection, Provider provider) : base(connection, provider) { }
    public NetQL(IDbConnection connection, Provider provider, bool leaveOpen) : base(connection, provider, leaveOpen) { }
}
#endif
