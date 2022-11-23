using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;

namespace netQL.Lib
{
    enum SqlModelType { INSERT, UPDATE, DELETE }
    public class SqlModel<T> : QueryCommon
    {
        private string query;
        private SqlModelType sqlModelType;
        private List<Set> columnValues;
        private DbUtils<T> dbUtils;
        private List<SetWhere> whereValues;
        private List<object> bulkInsertData;

        public SqlModel(DbUtils<T> dbUtils, string quotSql, string endQuotSql, string bindSymbol)
        {
            this.dbUtils = dbUtils;
            this.quotSql = quotSql;
            this.endQuotSql = endQuotSql;
            this.bindSymbol = bindSymbol;
            columnValues = new List<Set>();
            whereValues = new List<SetWhere>();
            bulkInsertData = new List<object>();
        }
        public SqlModel<T> Bulk(object dataBulk)
        {
            if (dataBulk is Array)
            {
                var dataList = (Array)dataBulk;
                foreach (var data in dataList)
                {
                    bulkInsertData.Add(data);
                }
            }
            else
            {
                bulkInsertData.Add(dataBulk);
            }
            return this;
        }
        public SqlModel<T> Insert(string tableName)
        {
            sqlModelType = SqlModelType.INSERT;
            query = "INSERT INTO " + quotSql + tableName + quotSql;
            return this;
        }
        public SqlModel<T> Update(string tableName)
        {
            sqlModelType = SqlModelType.UPDATE;
            query = "UPDATE " + quotSql + tableName + quotSql;
            return this;
        }
        public SqlModel<T> Delete(string tableName)
        {
            sqlModelType = SqlModelType.DELETE;
            query = "DELETE FROM " + quotSql + tableName + quotSql;
            return this;
        }
        public SqlModel<T> Where(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = GetType(value), CustomBind = customBind });
            return this;
        }
        public SqlModel<T> Where(string columnName, object value, DbType dbType, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = dbType, CustomBind = customBind });
            return this;
        }
        public SqlModel<T> Where(string columnName, object value, string oOperator, DbType dbType = DbType.String, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = dbType, CustomBind = customBind, ValueOperator = oOperator });
            return this;
        }
        public SqlModel<T> OrWhere(string columnName, object value, DbType dbType = DbType.String, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = dbType, CustomBind = customBind, Operator = "OR" });
            return this;
        }
        public SqlModel<T> OrWhere(string columnName, object value, string oOperator, DbType dbType = DbType.String, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhere { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = dbType, CustomBind = customBind, Operator = "OR", ValueOperator = oOperator });
            return this;
        }
        public SqlModel<T> AddValue(string columnName, object value, DbType dbType = DbType.String, Func<string, string> customBind = null)
        {
            columnValues.Add(new Set { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, VType = dbType, CustomBind = customBind });
            return this;
        }
        public SqlModel<T> AddRawValue(string columnName, string value)
        {
            columnValues.Add(new SetRaw { Column = columnName, BindName = columnName.Replace('.', '_'), Value = value, IsRaw = true });
            return this;
        }
        public SqlModel<T> SetValue(string columnName, object value, DbType dbType = DbType.String, Func<string, string> customBind = null)
        {
            AddValue(columnName, value, dbType, customBind);
            return this;
        }
        public SqlModel<T> SetRawValue(string columnName, string value)
        {
            AddRawValue(columnName, value);
            return this;
        }

        public dynamic Execute(Action<dynamic> callback = null)
        {
            if (sqlModelType == SqlModelType.INSERT)
                SetupInsertQuery();
            else if (sqlModelType == SqlModelType.UPDATE)
                SetupUpdateQuery();
            else if (sqlModelType == SqlModelType.DELETE)
                SetupDeleteQuery();
            else return null;

            dbUtils.Query(query);

            foreach (Set _value in columnValues)
            {
                if (_value.GetType() == typeof(SetRaw) && (_value as SetRaw).IsRaw)
                    continue;

                dbUtils.AddParameter(_value.BindName, _value.Value, _value.VType);
            }
            foreach (Set _value in whereValues)
            {
                dbUtils.AddParameter(_value.BindName, _value.Value, _value.VType);
            }
            Clear();
            return dbUtils.Execute(callback);
        }

        private void Clear()
        {
            whereValues.Clear();
            columnValues.Clear();
        }

        private void SetupUpdateQuery()
        {
            query += " SET ";
            var setQuery = string.Empty;

            foreach (Set _value in columnValues)
            {
                var bindingValue = string.Empty;
                if (_value.GetType() == typeof(SetRaw) && (_value as SetRaw).IsRaw)
                {
                    bindingValue = _value.Value.ToString();
                }
                else
                {
                    bindingValue = (_value.CustomBind != null ? _value.CustomBind(":" + _value.BindName) : ":" + _value.BindName);
                }
                setQuery += ',' + WrapQuot(_value.Column) + '=' + bindingValue;
            }
            query += setQuery.TrimStart(',');

            GenerateWhere();
        }
        private void SetupInsertQuery()
        {
            if (bulkInsertData.Count > 0)
            {
                query += GenerateBulkInsertBind();
            }
            else
            {
                string columns = string.Empty;
                string bindings = string.Empty;
                foreach (Set _value in columnValues)
                {
                    columns += ',' + WrapQuot(_value.Column);
                    if (_value.GetType() == typeof(SetRaw) && (_value as SetRaw).IsRaw)
                    {
                        bindings += "," + _value.Value;
                    }
                    else
                    {
                        bindings += "," + (_value.CustomBind != null ? _value.CustomBind(":" + _value.BindName) : ":" + _value.BindName);
                    }
                }
                query += "(" + columns.TrimStart(',') + ")";
                query += " VALUES (" + bindings.TrimStart(',') + ")";
            }
        }
        private string GenerateBulkInsertBind()
        {
            string resultValuesQuery = string.Empty;
            string insertColumn = "";
            var index = 0;
            foreach (var data in bulkInsertData)
            {
                resultValuesQuery += ",(";

                var properties = data.GetType().GetProperties().AsEnumerable();
                int propertiesLength = properties.Count();
                string colValues = "";

                for (int i = 0; i < propertiesLength; i++)
                {
                    var prop = properties.ElementAt(i);
                    var columnName = prop.Name + index;
                    var valueProp = prop.GetValue(data);

                    // field with underscore will skipped
                    if (columnName.First() == '_') continue;
                    else
                    {
                        AddValue(columnName, valueProp, GetType(valueProp));
                        colValues += "," + bindSymbol + columnName;
                    }
                    // generate column name for insert
                    if (index == 0)
                    {
                        insertColumn += "," + WrapQuot(prop.Name);
                    }
                }
                index++;
                resultValuesQuery += colValues.Substring(1) + ")";
            }
            return "(" + insertColumn.Substring(1) + ") VALUES " + resultValuesQuery.Substring(1);
        }
        private void SetupDeleteQuery()
        {
            GenerateWhere();
        }
        private void GenerateWhere()
        {
            var whereQuery = string.Empty;
            bool firstCondition = true;
            foreach (SetWhere _value in whereValues)
            {
                if (firstCondition) { _value.Operator = string.Empty; firstCondition = false; }
                var bindingValue = string.Empty;
                if (_value.GetType() == typeof(SetWhereRaw) && (_value as SetWhereRaw).IsRaw)
                {
                    bindingValue = _value.Value.ToString();
                }
                else
                {
                    bindingValue = (_value.CustomBind != null ? _value.CustomBind(":" + _value.BindName) : ":" + _value.BindName);
                }
                whereQuery += ' ' + _value.Operator + " " + WrapQuot(_value.Column) + " " + _value.ValueOperator + " " + bindingValue;
            }
            query += " WHERE" + whereQuery;
        }
    }
}
