using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;
using System.Transactions;
using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.SqlTypes;

/*
 Author by   : Muhamad Deva Arofi
 GitHub      : https://github.com/daevsoft
 Year        : 2022
*/
namespace Daevsoft.Lib
{
    public enum Provider
    {
        SqlServer, MySql, PostgreSQL, Oracle, SQLite
    }
    public class Str
    {
        public static bool IsRaw(ref string sql)
        {
            if (sql == null || sql.Length < 3) return false;
            if (sql[0] != '!' || sql[1] != '!') return false;
            sql = sql.Substring(2);
            return true;
        }
        public static string Raw(string sql)
        {
            return "!!" + sql;
        }
    }
    public partial class DbUtils : QueryCommon, IDisposable, IAsyncDisposable
    {
        private IDbConnection connection;
        private IDbCommand command;
        private IDbTransaction transaction;
        private List<SetWhere> whereValues;
        private List<Join> joinValues;
        private DbUtils parentDb;
        private static long nextIdentity;
        private string identity = "q" + System.Threading.Interlocked.Increment(ref nextIdentity);
        private List<string> groupByColumns;
        private bool useTransaction;
        private bool ownsTransaction;
        private bool ownsConnection = true;
        private bool disposed;
        private int executing;
        private System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Unspecified;
        private List<IDbDataParameter> additionalParameters;
        private bool keepAliveConnection;
        private string providerName;
        private string baseQuery;
        private CommandType commandType;
        private int? commandTimeout;
        private int limitLength;
        private int limitOffset;
        private bool limitOffsetSet;
        internal string ProviderName => providerName;
        internal bool HasTransaction => useTransaction || transaction != null;

        private void EnsureAvailable()
        {
            if (disposed) throw new ObjectDisposedException(GetType().Name);
            if (System.Threading.Volatile.Read(ref executing) != 0)
                throw new InvalidOperationException("A NetQL session cannot be modified or reused while an operation is running. Use a separate session per concurrent operation.");
            if (parentDb != null) parentDb.EnsureAvailable();
        }

        public void IsKeepAlive(bool keepAlive)
        {
            EnsureAvailable();
            keepAliveConnection = keepAlive;
        }

        public DbUtils CommandTimeout(int seconds)
        {
            EnsureAvailable();
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            commandTimeout = seconds;
            return this;
        }

        public void Dispose()
        {
            if (disposed) return;
            EnterOperation(true);
            Exception failure = null;
            try { CloseCore(); }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                disposed = true;
                try { Cleanup(failure, () => { if (ownsConnection) connection.Dispose(); }); }
                finally
                {
                    System.Threading.Volatile.Write(ref executing, 0);
                    GC.SuppressFinalize(this);
                }
            }
        }

        public List<SetWhere> GetWhereValues()
        {
            EnsureAvailable();
            return new List<SetWhere>(whereValues);
        }
        public DbUtils(string connectionString, Provider provider) : this(CreateConnection(connectionString, provider), provider)
        {
        }

        private static IDbConnection CreateConnection(string connectionString, Provider provider)
        {
            string[] names;
            switch (provider)
            {
                case Provider.SqlServer:
                    names = new[] { "Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient", "System.Data.SqlClient.SqlConnection, System.Data.SqlClient" };
                    break;
                case Provider.MySql:
                    names = new[] { "MySqlConnector.MySqlConnection, MySqlConnector", "MySql.Data.MySqlClient.MySqlConnection, MySql.Data" };
                    break;
                case Provider.PostgreSQL:
                    names = new[] { "Npgsql.NpgsqlConnection, Npgsql" };
                    break;
                case Provider.Oracle:
                    names = new[] { "Oracle.ManagedDataAccess.Client.OracleConnection, Oracle.ManagedDataAccess" };
                    break;
                case Provider.SQLite:
                    names = new[] { "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite", "System.Data.SQLite.SQLiteConnection, System.Data.SQLite" };
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
            foreach (var name in names)
            {
                var type = Type.GetType(name, false);
                if (type != null) return (IDbConnection)Activator.CreateInstance(type, connectionString);
            }
            throw new NotSupportedException("Install a compatible ADO.NET driver for " + provider + ", or pass an IDbConnection explicitly.");
        }

        public DbUtils(IDbConnection connection, Provider provider)
        {
            switch (provider)
            {
                case Provider.MySql:
                    Setup(connection, '`', '`', '@', "MySQL");
                    break;
                case Provider.PostgreSQL:
                    Setup(connection, '"', '"', ':', "PostgreSQL");
                    break;
                case Provider.Oracle:
                    Setup(connection, '"', '"', ':', "Oracle");
                    break;
                case Provider.SQLite:
                    Setup(connection, '"', '"', '@', "SQLite");
                    break;
                case Provider.SqlServer:
                    Setup(connection, '[', ']', '@', "SqlServer");
                    break;
                default:
                    throw new Exception("Provider not supported");
            }
        }
        public DbUtils(IDbConnection connection)
        {
            switch (connection.GetType().Name)
            {
                case "MySqlConnection":
                    Setup(connection, '`', '`', '@', "MySQL");
                    break;
                case "NpgsqlConnection":
                    Setup(connection, '"', '"', ':', "PostgreSQL");
                    break;
                case "OracleConnection":
                    Setup(connection, '"', '"', ':', "Oracle");
                    break;
                case "SqlConnection":
                    Setup(connection, '[', ']', '@', "SqlServer");
                    break;
                case "SqliteConnection":
                    Setup(connection, '"', '"', '@', "SQLite");
                    break;
                default:
                    throw new Exception("Provider not supported");
            }
        }
        public DbUtils(IDbConnection connection, Provider provider, bool leaveOpen) : this(connection, provider)
        {
            ownsConnection = !leaveOpen;
        }

        public DbUtils(IDbConnection connection, char quotSql, char bindSymbol = '@')
        {
            char endQuot = quotSql == '[' ? ']' : quotSql;
            string name = InferProviderName(connection);
            Setup(connection, quotSql, endQuot, bindSymbol, name);
        }

        private static string InferProviderName(IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            switch (connection.GetType().Name)
            {
                case "SqlConnection": return "SqlServer";
                case "MySqlConnection": return "MySQL";
                case "NpgsqlConnection": return "PostgreSQL";
                case "OracleConnection": return "Oracle";
                case "SqliteConnection":
                case "SQLiteConnection": return "SQLite";
                default: return null;
            }
        }

        private void Setup(IDbConnection connection, char quotSql, char endQuotSql, char bindSymbol, string providerName)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.quotSql = quotSql.ToString();
            this.endQuotSql = endQuotSql.ToString();
            whereValues = new List<SetWhere>();
            joinValues = new List<Join>();
            additionalParameters = new List<IDbDataParameter>();
            this.bindSymbol = bindSymbol.ToString();
            this.providerName = providerName;
        }

        public DbUtils AddParameter(string name, object value, DbType type = DbType.String)
        {
            EnsureAvailable();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A parameter name is required.", nameof(name));
            name = name.TrimStart('@', ':', '?');
            if (additionalParameters.Any(p => string.Equals(p.ParameterName, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Duplicate parameter name: " + name, nameof(name));
            var parameter = CreateParameter();
            parameter.DbType = type;
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            additionalParameters.Add(parameter);
            return this;
        }

        public DbUtils Query(string sql, CommandType commandType = CommandType.Text)
        {
            EnsureAvailable();
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is required.", nameof(sql));
            Clear();
            this.commandType = commandType;
            baseQuery = sql;
            return this;
        }
        public DbUtils Select(string table)
        {
            string sql = "SELECT * FROM " + WrapTable(table, providerName == "Oracle");
            return Query(sql);
        }
        public DbUtils Select(string[] columns, string table)
        {
            if (columns == null || columns.Length == 0)
                throw new ArgumentException("At least one selected column is required.", nameof(columns));
            string sql = "SELECT " + string.Join(",", columns.Select(x =>
                Str.IsRaw(ref x) ? x.ToString() : WrapQuot(WrapQuot(x), true)))
                + " FROM " + WrapTable(table, providerName == "Oracle");
            return Query(sql);
        }
        public DbUtils Select(string columns, string table)
        {
            if (string.IsNullOrWhiteSpace(columns))
                throw new ArgumentException("At least one selected column is required.", nameof(columns));
            if (Str.IsRaw(ref columns))
            {
                string sql = "SELECT " + columns + " FROM " + WrapTable(table, providerName == "Oracle");
                return Query(sql);
            }
            var xColumns = columns.Split(',').Select(x => x.Trim()).ToArray();
            return Select(xColumns, table);
        }
        public DbUtils WhereNotIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "NOT IN", subQuery);
        }
        public DbUtils WhereNotIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "NOT IN", "AND");
        }
        public DbUtils OrWhereNotIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "NOT IN", subQuery, "OR");
        }
        public DbUtils OrWhereNotIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "NOT IN", "OR");
        }
        public DbUtils WhereIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "IN", subQuery);
        }
        public DbUtils WhereIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "IN", "AND");
        }
        public DbUtils OrWhereIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "IN", subQuery, "OR");
        }
        public DbUtils OrWhereIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "IN", "OR");
        }
        private DbUtils InList<B>(string columnName, B[] values, string valueOperator, string operatorKeyword)
        {
            EnsureAvailable();
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0)
            {
                EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Value = valueOperator == "IN" ? "1 = 0" : "1 = 1", Operator = operatorKeyword, ValueOperator = string.Empty, IsRaw = true });
                return this;
            }

            var items = new List<Set>();
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                items.Add(new Set
                {
                    Column = columnName,
                    BindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count + "_" + i),
                    Value = value,
                    VType = GetType(value),
                });
            }
            EnsureAvailable();
            whereValues.Add(new SetWhereIn
            {
                Column = columnName,
                Operator = operatorKeyword,
                ValueOperator = valueOperator,
                Items = items,
            });
            return this;
        }
        public DbUtils OrWhere(object wheres)
        {
            if (wheres == null) throw new ArgumentNullException(nameof(wheres));
            if (wheres is Array)
            {
                throw new Exception("Bulk Update it's not support for array data");
            }
            var properties = wheres.GetType().GetProperties().AsEnumerable();
            int propertiesLength = properties.Count();

            for (int i = 0; i < propertiesLength; i++)
            {
                var prop = properties.ElementAt(i);

                var valueProp = prop.GetValue(wheres);
                var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var columnName = columnAttr != null ? columnAttr.Name : prop.Name;
                if (valueProp == null)
                {
                    OrNull(columnName);
                    continue;
                }
                else
                {
                    OrWhere(columnName, valueProp);
                }
            }
            return this;
        }
        public DbUtils Where(object wheres)
        {
            if (wheres == null) throw new ArgumentNullException(nameof(wheres));
            if (wheres is Array)
            {
                throw new Exception("Bulk Update it's not support for array data");
            }
            var properties = wheres.GetType().GetProperties().AsEnumerable();
            int propertiesLength = properties.Count();

            for (int i = 0; i < propertiesLength; i++)
            {
                var prop = properties.ElementAt(i);
                var valueProp = prop.GetValue(wheres);
                var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var columnName = columnAttr != null ? columnAttr.Name : prop.Name;
                if (valueProp == null)
                {
                    AndNull(columnName);
                    continue;
                }
                else
                {
                    Where(columnName, valueProp);
                }
            }
            return this;
        }
        public DbUtils Wrap(Func<DbUtils, DbUtils> wheres)
        {
            var dbClone = Clone();
            var subQuery = ValidateSubquery(dbClone, wheres(dbClone));

            EnsureAvailable();
            whereValues.Add(new SubWhere
            {
                Column = null,
                Operator = "AND",
                ValueOperator = string.Empty,
                Value = "(" + subQuery.GenerateWhere(true) + ")",
                SubDbUtil = subQuery,
                IsRaw = true,
            });
            return this;
        }
        public DbUtils OrWrap(Func<DbUtils, DbUtils> wheres)
        {
            var dbClone = Clone();
            var subQuery = ValidateSubquery(dbClone, wheres(dbClone));

            EnsureAvailable();
            whereValues.Add(new SubWhere
            {
                Column = null,
                Operator = "OR",
                ValueOperator = string.Empty,
                Value = "(" + subQuery.GenerateWhere(true) + ")",
                SubDbUtil = subQuery,
                IsRaw = true,
            });
            return this;
        }
        public DbUtils AndNull(string columnName)
        {
            return WhereRaw(columnName, " IS ", "NULL");
        }
        public DbUtils OrNull(string columnName)
        {
            return OrWhereRaw(columnName, " IS ", "NULL");
        }
        public DbUtils Where(string columnName, string valueOperator, Func<DbUtils, DbUtils> wheres, string oOperator = "AND")
        {
            var dbClone = Clone();
            var subQuery = ValidateSubquery(dbClone, wheres(dbClone));
            // set default operator WHERE
            if (whereValues.Count == 0) oOperator = "WHERE";

            EnsureAvailable();
            whereValues.Add(new SubWhere
            {
                Column = columnName,
                Operator = oOperator,
                ValueOperator = valueOperator,
                Value = "(" + subQuery.GenerateQuery() + ")",
                SubDbUtil = subQuery
            });
            return this;
        }
        private string BindColumnName(string columnName)
        {
            EnsureAvailable();
            return columnName != null ? FixBindName(columnName, "_" + identity + "_" + whereValues.Count) : null;
        }
        public DbUtils Where(string columnName, object value, Func<string, string> customBind)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind });
            return this;
        }
        public DbUtils Where(string columnName, object value, DbType type)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere
            {
                VType = type,
                BindName = bindName,
                Column = columnName,
                Operator = "AND",
                Value = value,
            });
            return this;
        }
        public DbUtils Where(string columnName, object value)
        {
            if (value == null)
            {
                return AndNull(columnName);
            }
            else
            {
                return Where(columnName, value, null);
            }
        }
        public DbUtils OrWhere(string columnName, object value)
        {
            if (value == null)
            {
                return OrNull(columnName);
            }
            else
            {
                return OrWhere(columnName, value, null);
            }
        }
        public DbUtils Where(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, ValueOperator = oOperator });
            return this;
        }
        public DbUtils OrWhere(string columnName, object value, DbType type)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere
            {
                VType = type,
                BindName = bindName,
                Column = columnName,
                Operator = "OR",
                Value = value,
            });
            return this;
        }
        public DbUtils OrWhere(string columnName, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR" });
            return this;
        }
        public DbUtils OrWhere(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator });
            return this;
        }
        public DbUtils WhereRaw(string whereQuery)
        {
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = null, BindName = null, Value = whereQuery, ValueOperator = string.Empty });
            return this;
        }
        public DbUtils OrWhereRaw(string whereQuery)
        {
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = null, BindName = null, Value = whereQuery, Operator = "OR", IsRaw = true, ValueOperator = string.Empty });
            return this;
        }
        public DbUtils OrWhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", IsRaw = true });
            return this;
        }
        public DbUtils OrWhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator, IsRaw = true });
            return this;
        }
        public DbUtils WhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true });
            return this;
        }
        public DbUtils WhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            string bindName = BindColumnName(columnName);
            EnsureAvailable();
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true, ValueOperator = oOperator });
            return this;
        }
        public DbUtils OrderBy(string columns, Order orderType = Order.ASC)
        {
            EnsureAvailable();
            if (!Enum.IsDefined(typeof(Order), orderType)) throw new ArgumentOutOfRangeException(nameof(orderType));
            string clause = string.Join(",", columns.Split(',').Select(x => WrapQuot(x) + " " + orderType));
            orderAdditional = string.IsNullOrEmpty(orderAdditional)
                ? " ORDER BY " + clause
                : orderAdditional + ", " + clause;
            return this;
        }
        public DbUtils Asc(string columns)
        {
            return OrderBy(columns, Order.ASC);
        }
        public DbUtils Desc(string columns)
        {
            return OrderBy(columns, Order.DESC);
        }
        public DbUtils Limit(int length = 1)
        {
            return Limit(0, length);
        }
        public DbUtils Limit(int start, int length = 1)
        {
            EnsureAvailable();
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            limitLength = length;
            limitOffset = start;
            limitOffsetSet = true;
            return this;
        }
        private DbUtils AddJoinSet(string tableName, string onColumn1, string onColumn2, JoinTypes type = JoinTypes.INNER)
        {
            EnsureAvailable();
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("A table is required.", nameof(tableName));
            if (tableName[0] != '(')
                tableName = WrapTable(tableName, providerName == "Oracle");
            Join joinObject;
            if (onColumn2 == null)
            {
                // new join raw
                joinObject = new Join { Table = tableName, OnRaw = onColumn1, JoinType = type };
            }
            else
            {
                // new join set
                joinObject = new Join { Table = tableName, OnColumn = WrapQuot(onColumn1), OnValue = WrapQuot(onColumn2), JoinType = type };
            }
            // collect into list
            joinValues.Add(joinObject);
            return this;
        }
        public DbUtils Join(string tableName, string onColumn1, string onColumn2 = null)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2);
        }
        public DbUtils LeftJoin(string tableName, string onColumn1, string onColumn2 = null)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils RightJoin(string tableName, string onColumn1, string onColumn2 = null)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        public void AttachParent(DbUtils parent)
        {
            EnsureAvailable();
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parentDb)
                if (ReferenceEquals(ancestor, this)) throw new ArgumentException("Cyclic query parents are not supported.", nameof(parent));
            parentDb = parent;
            if (parent != null) ownsConnection = false;
        }
        internal static DbUtils ValidateSubquery(DbUtils expected, DbUtils result)
        {
            if (!ReferenceEquals(expected, result))
                throw new ArgumentException("A subquery callback must return the supplied query builder.");
            return result;
        }

        public DbUtils Clone()
        {
            EnsureAvailable();
            var cloned = new DbUtils(connection, quotSql[0], bindSymbol[0]);
            cloned.providerName = providerName;
            cloned.AttachParent(this);
            return cloned;
        }
        public DbUtils Alias(string alias)
        {
            EnsureAvailable();
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentException("An alias is required.", nameof(alias));
            queryAlias = alias;
            return this;
        }
        private string queryAlias;
        private DbUtils NewJoin(Func<DbUtils, DbUtils> subQuery, string alias, string onColumn1, string onColumn2, JoinTypes joinType = JoinTypes.INNER)
        {
            DbUtils dbClone = Clone();
            dbClone = ValidateSubquery(dbClone, subQuery(dbClone));
            AddJoinSet("(subquery)", onColumn1, onColumn2, joinType);
            var join = joinValues[joinValues.Count - 1];
            join.SubQuery = dbClone;
            join.Alias = WrapQuot(alias ?? dbClone.queryAlias ?? dbClone.identity);
            return this;
        }
        private DbUtils NewJoin(Func<DbUtils, DbUtils> subQuery, string onColumn1, string onColumn2, JoinTypes joinType = JoinTypes.INNER)
        {
            return NewJoin(subQuery, null, onColumn1, onColumn2, joinType);
        }
        public DbUtils Join(Func<DbUtils, DbUtils> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2);
        }
        public DbUtils Join(Func<DbUtils, DbUtils> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2);
        }
        public DbUtils LeftJoin(Func<DbUtils, DbUtils> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils LeftJoin(Func<DbUtils, DbUtils> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils RightJoin(Func<DbUtils, DbUtils> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        public DbUtils RightJoin(Func<DbUtils, DbUtils> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        public string GenerateWhere(bool ignoreKeyword = false)
        {
            var sql = new System.Text.StringBuilder();
            foreach (var value in whereValues)
            {
                if (sql.Length > 0) sql.Append(' ').Append(value.Operator == "OR" ? "OR" : "AND").Append(' ');
                string operand;
                string valueOperator = value.ValueOperator.Trim();
                if (value is SetWhereIn chunked && providerName == "Oracle" && chunked.Items.Count > OracleInListLimit)
                {
                    bool notIn = valueOperator.Equals("NOT IN", StringComparison.OrdinalIgnoreCase);
                    var groups = new List<string>();
                    for (int i = 0; i < chunked.Items.Count; i += OracleInListLimit)
                        groups.Add(WrapQuot(value.Column) + " " + valueOperator + " (" + string.Join(",",
                            chunked.Items.Skip(i).Take(OracleInListLimit).Select(item => bindSymbol + item.BindName)) + ")");
                    sql.Append('(').Append(string.Join(notIn ? " AND " : " OR ", groups)).Append(')');
                    continue;
                }
                if (value is SetWhereIn list)
                    operand = "(" + string.Join(",", list.Items.Select(item => bindSymbol + item.BindName)) + ")";
                else if (value is SubWhere sub)
                    operand = "(" + (sub.Column == null ? sub.SubDbUtil.GenerateWhere(true) : sub.SubDbUtil.GenerateQuery()) + ")";
                else if (value is SetWhereRaw)
                    operand = Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture);
                else if ((value.Value == null || value.Value == DBNull.Value) && value.CustomBind == null && IsNullComparison(valueOperator))
                {
                    operand = "NULL";
                    valueOperator = valueOperator == "=" || valueOperator.Equals("IS", StringComparison.OrdinalIgnoreCase) ? "IS" : "IS NOT";
                }
                else
                    operand = value.CustomBind != null ? value.CustomBind(bindSymbol + value.BindName) : bindSymbol + value.BindName;
                if (!string.IsNullOrEmpty(value.Column)) sql.Append(WrapQuot(value.Column)).Append(' ');
                if (!string.IsNullOrEmpty(valueOperator)) sql.Append(valueOperator).Append(' ');
                sql.Append(operand);
            }
            return sql.Length == 0 ? string.Empty : (ignoreKeyword ? string.Empty : " WHERE ") + sql;
        }

        private static bool IsNullComparison(string valueOperator)
        {
            return valueOperator == "=" || valueOperator == "!=" || valueOperator == "<>"
                || valueOperator.Equals("IS", StringComparison.OrdinalIgnoreCase)
                || valueOperator.Equals("IS NOT", StringComparison.OrdinalIgnoreCase);
        }
        private string GenerateJoins()
        {
            if (joinValues.Count == 0) return string.Empty;

            var joinQuery = string.Empty;
            foreach (Join _value in joinValues)
            {
                var joinType = _value.JoinType.ToString();
                var onCondition = !string.IsNullOrEmpty(_value.OnRaw) ? _value.OnRaw : (_value.OnColumn + "=" + _value.OnValue);
                string table = _value.SubQuery == null ? _value.Table : "(" + _value.SubQuery.GenerateQuery() + ") " + _value.Alias;
                joinQuery += " " + joinType + " JOIN " + table + " ON " + onCondition;
            }
            return joinQuery;
        }
        private string GenerateGroupBy()
        {
            string groupBy = string.Empty;
            if (groupByColumns != null && groupByColumns.Count > 0)
            {
                groupBy += " GROUP BY " + string.Join(",", groupByColumns.Select(x => WrapQuot(x)));
            }
            return groupBy;
        }
        private sealed class RowColumn
        {
            internal string Column;
            internal Type PropertyType;
            internal Action<object, object> Setter;
        }

        private static class RowMapping<A>
        {
            internal static readonly Func<object> Create;
            internal static readonly RowColumn[] Columns;

            static RowMapping()
            {
                Columns = typeof(A).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(p => p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<NotMappedAttribute>() == null)
                    .Select(p => new RowColumn
                    {
                        Column = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name,
                        PropertyType = p.PropertyType,
                        Setter = CompileSetter(p),
                    })
                    .ToArray();
                Create = CompileFactory(typeof(A));
            }

            private static Func<object> CompileFactory(Type type)
            {
                try
                {
                    if (type.IsValueType)
                        return System.Linq.Expressions.Expression.Lambda<Func<object>>(
                            System.Linq.Expressions.Expression.Convert(
                                System.Linq.Expressions.Expression.Default(type), typeof(object))).Compile();
                    if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                        return () => Activator.CreateInstance(type);
                    return System.Linq.Expressions.Expression.Lambda<Func<object>>(
                        System.Linq.Expressions.Expression.Convert(
                            System.Linq.Expressions.Expression.New(type), typeof(object))).Compile();
                }
                catch (Exception)
                {
                    return () => Activator.CreateInstance(type);
                }
            }

            private static Action<object, object> CompileSetter(PropertyInfo property)
            {
                try
                {
                    // Value-type targets are already boxed; reflection keeps the boxed copy authoritative.
                    if (property.DeclaringType == null || property.DeclaringType.IsValueType)
                        return (target, value) => property.SetValue(target, value);

                    var target = System.Linq.Expressions.Expression.Parameter(typeof(object), "target");
                    var value = System.Linq.Expressions.Expression.Parameter(typeof(object), "value");
                    var assign = System.Linq.Expressions.Expression.Assign(
                        System.Linq.Expressions.Expression.Property(
                            System.Linq.Expressions.Expression.Convert(target, property.DeclaringType), property),
                        System.Linq.Expressions.Expression.Convert(value, property.PropertyType));
                    return System.Linq.Expressions.Expression.Lambda<Action<object, object>>(assign, target, value).Compile();
                }
                catch (Exception)
                {
                    return (target, value) => property.SetValue(target, value);
                }
            }
        }

        private A MapRow<A>(ReaderUtil<IDataReader> reader)
        {
            object data = RowMapping<A>.Create();
            foreach (var mapping in RowMapping<A>.Columns)
                if (reader.TryGetOrdinal(mapping.Column, out int ordinal))
                    mapping.Setter(data, ReaderUtil<IDataReader>.ConvertValue(reader.GetValue<object>(ordinal), mapping.PropertyType));
            return (A)data;
        }

        public List<A> ReadAsList<A>()
        {
            var list = new List<A>();
            Read(reader => list.Add(MapRow<A>(reader)));
            return list;
        }

        public IEnumerable<A> ReadAsArray<A>()
        {
            List<A> data = new List<A>();
            int rowCount = Read(reader =>
            {
                A row = reader.GetValue<A>(0);
                data.Add(row);
            });
            return data;
        }
        public A ReadSingle<A>()
        {
            A data = default;
            int rowCount = ReadFirst(reader =>
            {
                data = reader.GetValue<A>(0);
                return;
            });
            return rowCount > 0 ? data : default;
        }
        public A ReadAs<A>()
        {
            A data = default;
            ReadFirst(reader => data = MapRow<A>(reader));
            return data;
        }
        public string GenerateQuery()
        {
            if (disposed) throw new ObjectDisposedException(GetType().Name);
            return baseQuery + GenerateJoins() + GenerateWhere() + GenerateGroupBy() + GenerateOrder() + GenerateLimit();
        }

        public int Read(Action<ReaderUtil<IDataReader>> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return ReadRows(callback, false);
        }

        private int ReadFirst(Action<ReaderUtil<IDataReader>> callback) => ReadRows(callback, true);

        private int ReadRows(Action<ReaderUtil<IDataReader>> callback, bool firstOnly)
        {
            int rows = 0;
            StartScope(() =>
            {
                var dataReader = command.ExecuteReader();
                Exception failure = null;
                try
                {
                    var reader = new ReaderUtil<IDataReader>(dataReader);
                    while (dataReader.Read())
                    {
                        callback(reader);
                        rows++;
                        if (firstOnly) break;
                    }
                }
                catch (Exception error) { failure = error; throw; }
                finally { Cleanup(failure, dataReader.Dispose); }
            });
            return rows;
        }

        private void Clear()
        {
            whereValues.Clear();
            orderAdditional = string.Empty;
            limitLength = 0;
            limitOffset = 0;
            limitOffsetSet = false;
            joinValues.Clear();
            groupByColumns?.Clear();
            additionalParameters.Clear();
            baseQuery = null;
            queryAlias = null;
        }

        private void EnterOperation(bool allowClone = false)
        {
            EnsureAvailable();
            if (parentDb != null && !allowClone) throw new InvalidOperationException("Clones are query builders only. Create a separate session to execute independently.");
            if (System.Threading.Interlocked.CompareExchange(ref executing, 1, 0) != 0)
                throw new InvalidOperationException("A NetQL operation is already running.");
        }

        private void PrepareCommand()
        {
            if (string.IsNullOrWhiteSpace(baseQuery)) throw new InvalidOperationException("Call Query or Select before executing.");
            command = connection.CreateCommand();
            command.CommandType = commandType;
            command.CommandText = GenerateQuery();
            if (commandTimeout.HasValue) command.CommandTimeout = commandTimeout.Value;
            // Oracle binds by position by default, but nested queries need named binding.
            if (providerName == "Oracle")
            {
                var bindByName = command.GetType().GetProperty("BindByName");
                if (bindByName?.CanWrite == true) bindByName.SetValue(command, true);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in QueryParameters())
            {
                if (!names.Add(value.BindName)) throw new InvalidOperationException("Duplicate parameter name: " + value.BindName);
                var parameter = command.CreateParameter();
                parameter.ParameterName = value.BindName;
                parameter.DbType = value.VType;
                parameter.Value = value.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        private void StartScope(Action operation)
        {
            EnterOperation();
            Exception failure = null;
            try
            {
                PrepareCommand();
                OpenConnection();
                command.Transaction = transaction;
                operation();
            }
            catch (Exception error)
            {
                failure = error;
                AbortOwnedTransaction(error);
                throw;
            }
            finally { FinishOperation(failure); }
        }

        private void AbortOwnedTransaction(Exception original)
        {
            if (!ownsTransaction) return;
            try { EndTransaction(false); }
            catch (Exception cleanup) { original.Data["NetQL.RollbackError"] = cleanup; }
        }

        private void FinishOperation(Exception failure)
        {
            var current = command;
            command = null;
            Clear();
            try { Cleanup(failure, () => current?.Dispose(), () => { if (transaction == null) CloseConnection(); }); }
            finally { System.Threading.Volatile.Write(ref executing, 0); }
        }

        public DbUtils Transaction(bool useTransaction = true)
        {
            EnsureAvailable();
            if (transaction != null) throw new InvalidOperationException("Complete the active transaction before changing transaction mode.");
            this.useTransaction = useTransaction;
            return this;
        }

        public DbUtils Transaction(System.Data.IsolationLevel isolationLevel)
        {
            Transaction();
            this.isolationLevel = isolationLevel;
            return this;
        }

        public DbUtils Transaction(IDbTransaction dbTransaction)
        {
            EnsureAvailable();
            if (dbTransaction == null) throw new ArgumentNullException(nameof(dbTransaction));
            if (transaction != null || useTransaction) throw new InvalidOperationException("A transaction is already configured.");
            if (!ReferenceEquals(dbTransaction.Connection, connection) || connection.State != ConnectionState.Open)
                throw new ArgumentException("The transaction must belong to this open connection.", nameof(dbTransaction));
            transaction = dbTransaction;
            useTransaction = true;
            ownsTransaction = false;
            ownsConnection = false;
            return this;
        }

        public int ExecuteNonQuery()
        {
            int rows = 0;
            StartScope(() => rows = command.ExecuteNonQuery());
            return rows;
        }

        public dynamic Execute(Action<dynamic> callback = null)
        {
            StartScope(() =>
            {
                int rows = command.ExecuteNonQuery();
                callback?.Invoke(rows);
            });
            return transaction;
        }

        public dynamic Commit(bool force = true)
        {
            EnterOperation();
            try
            {
                if (!force && useTransaction) return transaction;
                EndTransaction(true);
                return transaction;
            }
            finally { System.Threading.Volatile.Write(ref executing, 0); }
        }

        private void EndTransaction(bool commit)
        {
            if (transaction != null && !ownsTransaction)
                throw new InvalidOperationException("The caller owns this transaction and must complete it directly.");
            var current = transaction;
            transaction = null;
            ownsTransaction = false;
            useTransaction = false;
            isolationLevel = System.Data.IsolationLevel.Unspecified;
            Exception failure = null;
            try
            {
                if (current != null)
                {
                    if (commit) current.Commit();
                    else if (current.Connection != null) current.Rollback();
                }
            }
            catch (Exception error) { failure = error; throw; }
            finally { Cleanup(failure, () => current?.Dispose(), () => CloseConnection()); }
        }
        private string GenerateOrder()
        {
            return orderAdditional == null ? string.Empty : orderAdditional;
        }
        private string GenerateLimit()
        {
            if (limitLength <= 0) return string.Empty;
            int offset = limitOffsetSet && limitOffset > 0 ? limitOffset : 0;

            switch (providerName)
            {
                case "SqlServer":
                    if (string.IsNullOrWhiteSpace(orderAdditional))
                        throw new InvalidOperationException("SQL Server paging requires an explicit OrderBy with a stable key.");
                    return " OFFSET " + offset + " ROWS FETCH NEXT " + limitLength + " ROWS ONLY";
                case "Oracle":
                    return " OFFSET " + offset + " ROWS FETCH NEXT " + limitLength + " ROWS ONLY";
                case null:
                    throw new NotSupportedException("Paging requires a known provider. Use an explicit Provider constructor.");
                default:
                    return offset > 0 ? " LIMIT " + limitLength + " OFFSET " + offset : " LIMIT " + limitLength;
            }
        }

        private IEnumerable<Set> QueryParameters()
        {
            foreach (var parameter in additionalParameters)
                yield return new Set { BindName = parameter.ParameterName, Value = parameter.Value, VType = parameter.DbType };
            foreach (var value in whereValues)
            {
                if (value is SubWhere sub)
                {
                    foreach (var parameter in sub.SubDbUtil.QueryParameters()) yield return parameter;
                }
                else if (value is SetWhereIn list)
                {
                    foreach (var parameter in list.Items) yield return parameter;
                }
                else if (!(value is SetWhereRaw)
                    && !((value.Value == null || value.Value == DBNull.Value) && value.CustomBind == null && IsNullComparison(value.ValueOperator.Trim())))
                    yield return value;
            }
            foreach (var join in joinValues)
                if (join.SubQuery != null)
                    foreach (var parameter in join.SubQuery.QueryParameters()) yield return parameter;
        }

        internal void CopyParametersTo(DbUtils target)
        {
            foreach (var parameter in QueryParameters())
                target.AddParameter(parameter.BindName, parameter.Value, parameter.VType);
        }

        private void OpenConnection()
        {
            if (transaction != null && (transaction.Connection == null || connection.State != ConnectionState.Open))
                throw new InvalidOperationException("The attached transaction has completed or its connection is closed.");
            if (connection.State != ConnectionState.Open) connection.Open();
            if (useTransaction && transaction == null)
            {
                transaction = connection.BeginTransaction(isolationLevel);
                ownsTransaction = true;
            }
        }

        public IDbDataParameter CreateParameter()
        {
            EnsureAvailable();
            using (var temporary = connection.CreateCommand()) return temporary.CreateParameter();
        }

        public void Close(bool forceClose = false)
        {
            EnterOperation(true);
            try { CloseCore(forceClose); }
            finally { System.Threading.Volatile.Write(ref executing, 0); }
        }

        private void CloseCore(bool forceClose = false)
        {
            Exception failure = null;
            try { if (ownsTransaction) EndTransaction(false); }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                transaction = null;
                useTransaction = false;
                var current = command;
                command = null;
                Clear();
                Cleanup(failure, () => current?.Dispose(), () => CloseConnection(forceClose));
            }
        }

        private static void Cleanup(Exception original, params Action[] actions)
        {
            Exception failure = original;
            for (int i = 0; i < actions.Length; i++)
            {
                try { actions[i](); }
                catch (Exception cleanup)
                {
                    if (failure == null) failure = cleanup;
                    else failure.Data["NetQL.CleanupError." + i] = cleanup;
                }
            }
            if (original == null && failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void CloseConnection(bool forceClose = false)
        {
            if (parentDb == null && (ownsConnection || forceClose) && connection.State != ConnectionState.Closed && (!keepAliveConnection || forceClose))
                connection.Close();
        }

        public void Rollback()
        {
            EnterOperation();
            try { EndTransaction(false); }
            finally { System.Threading.Volatile.Write(ref executing, 0); }
        }
        private void InitGroupBy()
        {
            if (groupByColumns == null)
                groupByColumns = new List<string>();
        }
        public DbUtils GroupBy(string columnName)
        {
            EnsureAvailable();
            InitGroupBy();
            groupByColumns.Add(columnName);
            return this;
        }
        public DbUtils GroupBy(List<string> columnNames)
        {
            EnsureAvailable();
            groupByColumns = new List<string>(columnNames ?? throw new ArgumentNullException(nameof(columnNames)));
            return this;
        }
        public bool IsExist()
        {
            return ReadFirst(_ => { }) != 0;
        }
        public SqlModel Insert(string tableName)
        {
            return new SqlModel(this, quotSql, endQuotSql, bindSymbol).Insert(tableName);
        }
        public SqlModel Insert<A>(string tableName, A dataObject)
        {
            var _dbUtilTemp = new SqlModel(this, quotSql, endQuotSql, bindSymbol).Insert(tableName);
            if (dataObject is IEnumerable && !(dataObject is string))
            {
                _dbUtilTemp.Bulk(dataObject);
            }
            else
            {
                _dbUtilTemp.BindRow(dataObject);
            }
            return _dbUtilTemp;
        }
        public SqlModel Update(string tableName)
        {
            return new SqlModel(this, quotSql, endQuotSql, bindSymbol).Update(tableName);
        }
        public SqlModel Delete(string tableName)
        {
            return new SqlModel(this, quotSql, endQuotSql, bindSymbol).Delete(tableName);
        }
    }
}
