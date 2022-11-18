using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;
using System.Transactions;

/*
 Author by   : Muhamad Deva Arofi
 GitHub      : https://github.com/daevsoft
 Year        : 2022
*/
namespace netQL.Lib
{
    public enum Provider
    {
        SqlServer, MySql, PostgreSQL
    }

    public partial class DbUtils<T> : QueryCommon
    {
        private dynamic connection;
        private dynamic command;
        private dynamic transaction;
        private List<SetWhere> whereValues;
        private List<Join> joinValues;
        private DbUtils<T> parentDb;
        private string identity = new Random().Next(200).ToString();
        public DbUtils(T connection, Provider provider)
        {
            if(provider == Provider.MySql)
            {
                Setup(connection, '`', '`', '@');
            }else if(provider == Provider.PostgreSQL)
            {
                Setup(connection, '"', '"', ':');
            }
            else if (provider == Provider.SqlServer)
            {
                Setup(connection, '[', ']', '@');
            }
            else
            {
                throw new Exception("Provider not supported");
            }
        }
        public DbUtils(T connection, char quotSql = '`', char bindSymbol = '@')
        {
            char endQuot = quotSql;
            if (quotSql == '[')
                endQuot = ']';
            Setup(connection, quotSql, endQuot, bindSymbol);
        }
        private void Setup(T connection, char quotSql, char endQuotSql, char bindSymbol)
        {
            this.connection = connection;
            command = this.connection.CreateCommand();
            this.quotSql = quotSql.ToString();
            this.endQuotSql = endQuotSql.ToString();
            whereValues = new List<SetWhere>();
            joinValues = new List<Join>();
            this.bindSymbol = bindSymbol.ToString();
        }
        public DbUtils<T> AddParameter(string name, object value, DbType type = DbType.String)
        {
            var newDbParam = command.CreateParameter();
            newDbParam.DbType = type;
            newDbParam.ParameterName = name;
            newDbParam.Value = value ?? DBNull.Value;
            command.Parameters.Add(newDbParam);
            return this;
        }

        public DbUtils<T> Query(string sql, CommandType commandType = CommandType.Text)
        {
            OpenConnection();
            command.CommandType = commandType;
            command.CommandText = sql;
            command.Parameters.Clear();
            return this;
        }
        public DbUtils<T> Select(string table)
        {
            string sql = "SELECT * FROM " + WrapQuot(table);
            return Query(sql);
        }
        public DbUtils<T> Select(string[] columns, string table)
        {
            string sql = "SELECT " + string.Join(',', columns.Select(x =>
            WrapQuot(WrapQuot(x), true)))
                + " FROM " + WrapQuot(table);
            return Query(sql);
        }
        public DbUtils<T> Select(string columns, string table)
        {
            var xColumns = columns.Split(',').Select(x => x.Trim()).ToArray();
            return Select(xColumns, table);
        }

        public DbUtils<T> Where(Func<DbUtils<T>, DbUtils<T>> wheres)
        {
            var dbClone = Clone();
            var getWheres = wheres(dbClone);
            whereValues.AddRange(getWheres.whereValues);
            return this;
        }
        public DbUtils<T> Where(string columnName, object value, Func<string, string> customBind)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind });
            return this;
        }
        public DbUtils<T> Where(string columnName, object value)
        {
            Where(columnName, value, null);
            return this;
        }
        public DbUtils<T> Where(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, ValueOperator = oOperator });
            return this;
        }
        public DbUtils<T> OrWhere(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR" });
            return this;
        }
        public DbUtils<T> OrWhere(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator });
            return this;
        }
        public DbUtils<T> OrWhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", IsRaw = true });
            return this;
        }
        public DbUtils<T> OrWhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator, IsRaw = true });
            return this;
        }
        public DbUtils<T> WhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true });
            return this;
        }
        public DbUtils<T> WhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName.Replace('.', '_') + "_" + identity, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true, ValueOperator = oOperator });
            return this;
        }
        public DbUtils<T> OrderBy(string columns, Order orderType)
        {
            orderAdditional = " ORDER BY " + string.Join(',', columns.Split(',').Select(x => WrapQuot(x))) + " " + orderType.ToString();
            return this;
        }
        public DbUtils<T> Asc(string columns)
        {
            return OrderBy(columns, Order.ASC);
        }
        public DbUtils<T> Desc(string columns)
        {
            return OrderBy(columns, Order.DESC);
        }
        public DbUtils<T> Limit(int length = 1)
        {
            limitAdditional = " LIMIT " + length.ToString();
            return this;
        }
        public DbUtils<T> Limit(int start, int length = 1)
        {
            limitAdditional = " LIMIT " + length.ToString() + " OFFSET " + start;
            return this;
        }
        private DbUtils<T> AddJoinSet(string tableName, string onColumn1, string onColumn2, JoinTypes type = JoinTypes.INNER)
        {
            // check if tableName is sub query or not
            if (tableName[0] != '(')
                tableName = WrapQuot(tableName);

            // new join set
            var joinObject = new Join { Table = tableName, OnColumn = WrapQuot(onColumn1), OnValue = WrapQuot(onColumn2), JoinType = type };

            // collect into list
            joinValues.Add(joinObject);
            return this;
        }
        public DbUtils<T> Join(string tableName, string onColumn1, string onColumn2)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2);
        }
        public DbUtils<T> LeftJoin(string tableName, string onColumn1, string onColumn2)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils<T> RightJoin(string tableName, string onColumn1, string onColumn2)
        {
            return AddJoinSet(tableName, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        public void AttachParent(DbUtils<T> parent)
        {
            parentDb = parent;
        }
        public DbUtils<T> Clone()
        {
            char quot = this.quotSql == null ? '\'' : this.quotSql[0];
            var cloned = new DbUtils<T>(connection, quot);
            cloned.AttachParent(this);
            return cloned;
        }
        public DbUtils<T> Alias(string alias)
        {
            identity = alias;
            return this;
        }
        private DbUtils<T> NewJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string alias, string onColumn1, string onColumn2, JoinTypes joinType = JoinTypes.INNER)
        {
            DbUtils<T> dbClone = Clone();
            dbClone = subQuery(dbClone);
            dbClone.identity = alias;
            string queryTarget = dbClone.generateQuery();
            return AddJoinSet("(" + queryTarget + ") " + dbClone.identity, onColumn1, onColumn2);
        }
        private DbUtils<T> NewJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string onColumn1, string onColumn2, JoinTypes joinType = JoinTypes.INNER)
        {
            DbUtils<T> dbClone = Clone();
            dbClone = subQuery(dbClone);
            string queryTarget = dbClone.generateQuery();
            return AddJoinSet("(" + queryTarget + ") " + dbClone.identity, onColumn1, onColumn2);
        }
        public DbUtils<T> Join(Func<DbUtils<T>, DbUtils<T>> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2);
        }
        public DbUtils<T> Join(Func<DbUtils<T>, DbUtils<T>> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2);
        }
        public DbUtils<T> LeftJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils<T> LeftJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2, JoinTypes.LEFT);
        }
        public DbUtils<T> RightJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        public DbUtils<T> RightJoin(Func<DbUtils<T>, DbUtils<T>> subQuery, string alias, string onColumn1, string onColumn2)
        {
            return NewJoin(subQuery, alias, onColumn1, onColumn2, JoinTypes.RIGHT);
        }
        private string GenerateWhere()
        {
            if (whereValues.Count == 0) return string.Empty;

            var whereQuery = string.Empty;
            bool firstCondition = true;
            foreach (SetWhere _value in whereValues)
            {
                if (_value.IsFromChild) continue;

                if (firstCondition) { _value.Operator = string.Empty; firstCondition = false; }

                var bindingValue = _value.Value;

                if (_value.GetType() == typeof(SetWhere))
                    bindingValue = _value.CustomBind != null ? _value.CustomBind(bindSymbol + _value.BindName) : bindSymbol + _value.BindName;

                whereQuery += ' ' + _value.Operator + " " + WrapQuot(_value.Column) + " " + _value.ValueOperator + " " + bindingValue;
            }
            return String.IsNullOrEmpty(whereQuery) ? whereQuery : " WHERE" + whereQuery;
        }
        private string GenerateJoins()
        {
            if (joinValues.Count == 0) return string.Empty;

            var joinQuery = string.Empty;
            foreach (Join _value in joinValues)
            {
                var joinType = _value.JoinType.ToString();
                joinQuery += " " + joinType +
                    " JOIN " + _value.Table + " ON " + _value.OnColumn + "=" + _value.OnValue;
            }
            return joinQuery;
        }
        public List<A> ReadAsList<A>()
        {
            List<A> list = new List<A>();
            Read(reader =>
            {
                var data = (A)Activator.CreateInstance(typeof(A));
                var properties = data.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    dynamic value;
                    if (reader.IsColumnExist(prop.Name))
                    {
                        if (prop.PropertyType == typeof(int))
                        {
                            value = reader.GetValue<int>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(short))
                        {
                            value = reader.GetValue<short>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(DateTime))
                        {
                            value = reader.GetValue<DateTime>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(double))
                        {
                            value = reader.GetValue<double>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(long))
                        {
                            value = reader.GetValue<long>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(char))
                        {
                            value = reader.GetValue<char>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(bool))
                        {
                            value = reader.GetValue<bool>(prop.Name);
                        }
                        else
                        {
                            value = reader.GetValue<string>(prop.Name);
                        }
                        prop.SetValue(data, Convert.ChangeType(value, prop.PropertyType), null);
                    }
                }
                list.Add(data);
            });
            return list;
        }
        public A ReadAs<A>()
        {
            var data = (A)Activator.CreateInstance(typeof(A));
            int rowCount = Read(reader =>
            {
                var properties = data.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    dynamic value;
                    if (reader.IsColumnExist(prop.Name))
                    {
                        if (prop.PropertyType == typeof(int))
                        {
                            value = reader.GetValue<int>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(short))
                        {
                            value = reader.GetValue<short>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(DateTime))
                        {
                            value = reader.GetValue<DateTime>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(double))
                        {
                            value = reader.GetValue<double>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(long))
                        {
                            value = reader.GetValue<long>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(char))
                        {
                            value = reader.GetValue<char>(prop.Name);
                        }
                        else if (prop.PropertyType == typeof(bool))
                        {
                            value = reader.GetValue<bool>(prop.Name);
                        }
                        else
                        {
                            value = reader.GetValue<string>(prop.Name);
                        }
                        prop.SetValue(data, Convert.ChangeType(value, prop.PropertyType), null);
                    }
                }
                return;
            });
            return rowCount > 0 ? data : default;
        }
        public String generateQuery()
        {
            AddOptionJoin();
            AddOptionWhere();
            AddOptionOrder();
            AddOptionLimit();
            string query = command.CommandText;
            if (parentDb != null)
                parentDb = null;
            return query;
        }
        public int Read(Action<ReaderUtil<IDataReader>> callback)
        {
            // check if contain where conditions
            AddOptionJoin();
            AddOptionWhere();
            AddOptionOrder();
            AddOptionLimit();

            int rows = 0;
            StartScope(() =>
            {
                var dataReader = command.ExecuteReader();
                if (dataReader.HasRows)
                {
                    while (dataReader.Read())
                    {
                        ReaderUtil<IDataReader> reader = new ReaderUtil<IDataReader>(dataReader);
                        callback(reader);
                        rows++;
                    }
                    dataReader.Close();
                }
                Close();
            });
            return rows;
        }

        private void Clear()
        {
            whereValues.Clear();
            orderAdditional =
            limitAdditional = string.Empty;
            joinValues.Clear();
        }

        private void StartScope(Action transactionCallback)
        {
            using (TransactionScope scope = new TransactionScope())
            {
                try
                {
                    transactionCallback();

                    // complete scope transaction
                    scope.Complete();
                }
                catch (Exception e)
                {
                    scope.Complete();
                    throw;
                }
            }
        }

        public dynamic Execute(Action<dynamic> callback = null)
        {
            StartScope(() =>
            {
                transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                var result = command.ExecuteNonQuery();
                transaction.Commit();
                Close();

                if (callback != null)
                    callback(result);

            });
            return transaction;
        }

        private void AddOptionOrder()
        {
            command.CommandText += orderAdditional;
        }
        private void AddOptionLimit()
        {
            command.CommandText += limitAdditional;
        }
        private void AddOptionWhere()
        {
            command.CommandText += GenerateWhere();

            foreach (SetWhere _value in whereValues)
            {
                if (_value.GetType() == typeof(SetWhereRaw))
                    continue;

                AddParameter(_value.BindName, _value.Value, _value.VType);
                if (parentDb != null)
                {
                    _value.IsFromChild = true;
                    parentDb.whereValues.Add(_value);
                }
            }
        }

        private void AddOptionJoin()
        {
            command.CommandText += GenerateJoins();
        }

        private void OpenConnection()
        {
            CloseConnection();
            connection.Open();
        }
        public void Close()
        {
            CloseConnection();
            Clear();
        }

        private void CloseConnection()
        {
            if (connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
        public void Rollback()
        {
            if (transaction != null)
                transaction.Rollback();
        }
        public bool IsExist()
        {
            int rows = 0;
            StartScope(() =>
            {
                AddOptionWhere();
                command.CommandText = "select 1 from (" + command.CommandText + ") anu group by 1";
                var dataReader = command.ExecuteReader();
                if (dataReader.HasRows)
                {
                    while (dataReader.Read())
                    {
                        rows++;
                    }
                    dataReader.Close();
                }
                Close();
            });
            return rows > 0;
        }
        public SqlModel<T> Insert(string tableName)
        {
            return new SqlModel<T>(this, quotSql).Insert(tableName);
        }
        private void BindProperties<A>(SqlModel<T> dbUtil, A dataObject)
        {
            if (dataObject == null) return;

            var properties = dataObject.GetType().GetProperties().AsEnumerable();
            int propertiesLength = properties.Count();

            for (int i = 0; i < propertiesLength; i++)
            {
                var prop = properties.ElementAt(i);
                var columnName = prop.Name;
                var typeProp = prop.PropertyType;
                var valueProp = prop.GetValue(dataObject);

                // field with underscore will skipped
                if (columnName.First() == '_') continue;
                else
                {
                    dbUtil.AddValue(columnName, valueProp, GetType(valueProp));
                }
            }
        }
        public SqlModel<T> Insert<A>(string tableName, A dataObject)
        {
            var _dbUtilTemp = new SqlModel<T>(this, quotSql).Insert(tableName);
            BindProperties(_dbUtilTemp, dataObject);
            return _dbUtilTemp;
        }
        public SqlModel<T> Update(string tableName)
        {
            return new SqlModel<T>(this, quotSql).Update(tableName);
        }
        public SqlModel<T> Delete(string tableName)
        {
            return new SqlModel<T>(this, quotSql).Delete(tableName);
        }
    }
}
