using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Daevsoft.Lib
{
    public class QueryCommon
    {
        // Oracle rejects IN lists with more than 1000 expressions (ORA-01795).
        internal const int OracleInListLimit = 1000;
        private static readonly Regex NonParameterCharacter = new Regex("[^a-zA-Z0-9_]", RegexOptions.Compiled);
        public enum Order { ASC, DESC }

        protected string? orderAdditional;
        protected string? quotSql;
        protected string? endQuotSql;
        protected string? bindSymbol;

        protected string QuoteIdentifierPart(string text)
        {
            if (text == "*") return text;
            if (text.StartsWith(quotSql, StringComparison.Ordinal) && text.EndsWith(endQuotSql, StringComparison.Ordinal) && text.Length >= 2)
                text = text.Substring(1, text.Length - 2).Replace(endQuotSql + endQuotSql, endQuotSql);
            return quotSql + text.Replace(endQuotSql, endQuotSql + endQuotSql) + endQuotSql;
        }

        private List<string> SplitName(string name, bool splitDots)
        {
            var parts = new List<string>();
            var part = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                if (!quoted && (part.Length == 0 || part[part.Length - 1] == '.') && character == quotSql[0]) quoted = true;
                else if (quoted && character == endQuotSql[0])
                {
                    if (i + 1 < name.Length && name[i + 1] == character)
                    {
                        part.Append(character).Append(character);
                        i++;
                        continue;
                    }
                    quoted = false;
                }
                if (!quoted && (splitDots ? character == '.' : char.IsWhiteSpace(character)))
                {
                    if (part.Length > 0) { parts.Add(part.ToString()); part.Clear(); }
                    else if (splitDots) throw new ArgumentException("An identifier component is empty.", nameof(name));
                }
                else part.Append(character);
            }
            if (quoted) throw new ArgumentException("An identifier has an unmatched quote.", nameof(name));
            if (part.Length > 0) parts.Add(part.ToString());
            else if (splitDots) throw new ArgumentException("An identifier component is empty.", nameof(name));
            return parts;
        }

        protected string WrapQuot(string name, bool reverseQuot = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (Str.IsRaw(ref name) || (name.Contains('(') && name.Contains(')'))) return name;
            var tokens = SplitName(name.Trim(), false);
            if (tokens.Count > 3 || (tokens.Count == 3 && !tokens[1].Equals("AS", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Use an identifier with an optional alias, or explicit raw SQL.", nameof(name));
            var identifier = string.Join(".", SplitName(tokens[0], true).Select(QuoteIdentifierPart));
            if (tokens.Count == 1) return identifier;
            return identifier + (tokens.Count == 3 ? " AS " : " ") + QuoteIdentifierPart(tokens[tokens.Count - 1]);
        }

        protected string WrapTable(string name, bool oracle)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A table is required.", nameof(name));
            if (Str.IsRaw(ref name) || (name.Contains('(') && name.Contains(')'))) return name;
            var tokens = SplitName(name.Trim(), false);
            if (oracle && tokens.Count == 3 && tokens[1].Equals("AS", StringComparison.OrdinalIgnoreCase))
                name = tokens[0] + " " + tokens[2];
            return WrapQuot(name);
        }

        protected string FixBindName(string columnName, string additional = "")
        {
            var name = NonParameterCharacter.Replace(columnName ?? "value", "_");
            if (name.Length > 10) name = name.Substring(0, 10);
            return "p" + name + additional;
        }

        protected DbType GetType(Type typeProp)
        {
            typeProp = Nullable.GetUnderlyingType(typeProp) ?? typeProp;
            if (typeProp.IsEnum) typeProp = Enum.GetUnderlyingType(typeProp);
            if (typeProp == typeof(byte[])) return DbType.Binary;
            if (typeProp == typeof(Guid)) return DbType.Guid;
            if (typeProp == typeof(DateTimeOffset)) return DbType.DateTimeOffset;
            if (typeProp == typeof(TimeSpan)) return DbType.Time;
            switch (Type.GetTypeCode(typeProp))
            {
                case TypeCode.Boolean: return DbType.Boolean;
                case TypeCode.Byte: return DbType.Byte;
                case TypeCode.SByte: return DbType.SByte;
                case TypeCode.Int16: return DbType.Int16;
                case TypeCode.UInt16: return DbType.UInt16;
                case TypeCode.Int32: return DbType.Int32;
                case TypeCode.UInt32: return DbType.UInt32;
                case TypeCode.Int64: return DbType.Int64;
                case TypeCode.UInt64: return DbType.UInt64;
                case TypeCode.Single: return DbType.Single;
                case TypeCode.Double: return DbType.Double;
                case TypeCode.Decimal: return DbType.Decimal;
                case TypeCode.DateTime: return DbType.DateTime;
                case TypeCode.Char: return DbType.StringFixedLength;
                default: return DbType.String;
            }
        }

        protected DbType GetType(object value) => value == null ? DbType.String : GetType(value.GetType());

        protected enum JoinTypes { INNER = 0, LEFT = 1, RIGHT = 2 }
        protected class Join
        {
            public JoinTypes JoinType { set; get; }
            public string Table { set; get; }
            public string OnColumn { set; get; }
            public string OnValue { set; get; }
            public string OnRaw { set; get; }
            public DbUtils SubQuery { set; get; }
            public string Alias { set; get; }
        }

        public class Set
        {
            public bool IsFromChild { set; get; }
            public string Column { set; get; }
            public string BindName { set; get; }
            public DbType VType { set; get; }
            public object Value { set; get; }
            public Func<string, string> CustomBind { set; get; }
        }
        public class SetWhere : Set
        {
            public string ValueOperator = "=";
            public string Operator = "AND";
        }
        public class SetWhereRaw : SetWhere
        {
            public bool IsRaw { set; get; }
        }
        public class SetWhereIn : SetWhere
        {
            public List<Set> Items { set; get; }
        }
        protected class SubWhere : SetWhereRaw
        {
            public DbUtils SubDbUtil { set; get; }
        }
        protected class SetRaw : Set
        {
            public bool IsRaw { set; get; }
        }
    }
}
