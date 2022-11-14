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
        public SqlModel(DbUtils<T> dbUtils, string quotSql)
        {
            this.dbUtils = dbUtils;
            this.quotSql = quotSql;
            columnValues = new List<Set>();
            whereValues = new List<SetWhere>();
        }
        public SqlModel<T> Insert(string tableName)
        {
            sqlModelType = SqlModelType.INSERT;
            query = "INSERT INTO " + WrapQuot(tableName);
            return this;
        }
        public SqlModel<T> Update(string tableName)
        {
            sqlModelType = SqlModelType.UPDATE;
            query = "UPDATE " + WrapQuot(tableName);
            return this;
        }
        public SqlModel<T> Delete(string tableName)
        {
            sqlModelType = SqlModelType.DELETE;
            query = "DELETE FROM " + WrapQuot(tableName);
            return this;
        }
        public SqlModel<T> Where(string columnName, object value, DbType dbType = DbType.String, Func<string, string> customBind = null)
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
                    bindingValue = _value.CustomBind != null ? _value.CustomBind(bindSymbol + _value.BindName) : bindSymbol + _value.BindName;
                }
                setQuery += ',' + WrapQuot(_value.Column) + '=' + bindingValue;
            }
            query += setQuery.TrimStart(',');

            GenerateWhere();
        }
        private void SetupInsertQuery()
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
                    bindings += "," + (_value.CustomBind != null ? _value.CustomBind(bindSymbol + _value.BindName) : bindSymbol + _value.BindName);
                }
            }
            query += "(" + columns.TrimStart(',') + ")";
            query += " VALUES(" + bindings.TrimStart(',') + ")";
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
                    bindingValue = _value.CustomBind != null ? _value.CustomBind(bindSymbol + _value.BindName) : bindSymbol + _value.BindName;
                }
                whereQuery += ' ' + _value.Operator + " " + WrapQuot(_value.Column) + " " + _value.ValueOperator + " " + bindingValue;
            }
            query += " WHERE" + whereQuery;
        }
    }
}
