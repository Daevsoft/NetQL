using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;

namespace netQL.Lib
{

    public class QueryCommon
    {
        public enum Order { ASC, DESC }

        protected string? orderAdditional;
        protected string? limitAdditional;
        protected string? quotSql;
        protected string? endQuotSql;
        protected string? bindSymbol;
        private string FixQuot(string text)
        {
            if (text.Trim() == "*" || text[0] == quotSql?[0])
            {
                return text;
            }
            return quotSql + text + endQuotSql;
        }
        protected string WrapQuot(string name, bool reverseQuot = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            if (name.Contains('.'))
            {
                string columnName = name.Substring(name.IndexOf('.') + 1);
                var columnAlias = columnName.Split(' ').Select(x => FixQuot(x)).ToArray();
                return name.Substring(0, name.IndexOf('.') + 1) + string.Join(' ', columnAlias);
            }
            if (name.Contains(' '))
            {
                string text = !reverseQuot ?
                    name.Substring(0, name.IndexOf(' '))
                    : name.Substring(name.IndexOf(' ') + 1);
                return name.Replace(text, FixQuot(text));
            }
            if (!reverseQuot)
                return FixQuot(name);
            else return name;
        }
        protected DbType GetType(object value)
        {
            Type typeProp = value.GetType();
            if (typeProp == typeof(string))
            {
                return DbType.String;
            }
            else if (typeProp == typeof(int))
            {
                return DbType.Int32;
            }
            else if (typeProp == typeof(long))
            {
                return DbType.Int64;
            }
            else if (typeProp == typeof(DateTime))
            {
                return DbType.DateTime;
            }
            else if (typeProp == typeof(double))
            {
                return DbType.Double;
            }
            else if (typeProp == typeof(bool))
            {
                return DbType.Boolean;
            }
            return DbType.String;
        }
        protected enum JoinTypes { INNER = 0, LEFT = 1, RIGHT = 2 }
        protected class Join
        {
            public JoinTypes JoinType { set; get; }
            public string Table { set; get; }
            public string OnColumn { set; get; }
            public string OnValue { set; get; }
        }

        protected class Set
        {
            public bool IsFromChild { set; get; }
            public string Column { set; get; }
            public string BindName { set; get; }
            public DbType VType { set; get; }
            public object Value { set; get; }
            public Func<string, string> CustomBind { set; get; }
        }
        protected class SetWhere : Set
        {
            public string ValueOperator = "=";
            public string Operator = "AND"; // AND OR
        }
        protected class SetWhereRaw : SetWhere
        {
            public bool IsRaw { set; get; }
        }
        protected class SetRaw : Set
        {
            public bool IsRaw { set; get; }
        }
    }
}
