using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;

namespace netQL.Lib
{
    public class ReaderUtil<T>
    {
        DbDataReader reader;
        public ReaderUtil(DbDataReader reader)
        {
            this.reader = reader;
        }
        public bool IsColumnExist(string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName) != -1;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private string FixNumeric<A>(object value)
        {
            if (value == null) return "0";
            var _value = value.ToString();
            if (typeof(A) != typeof(double) && typeof(A) != typeof(float))
            {
                if (_value.Contains('.'))
                    _value = _value.Substring(0, _value.IndexOf('.'));
                if (_value.Contains(','))
                    _value = _value.Substring(0, _value.IndexOf(','));
            }

            return _value;
        }
        public D GetValue<D>(int ordinal)
        {
            dynamic? value = GetValue<D>(reader.GetColumnSchema().ElementAt(ordinal).ColumnName);
            return ConvertValue<D>(value);
        }
        public D GetValue<D>(string columnName)
        {
            dynamic? value = reader.GetValue(columnName) == DBNull.Value
                        ? null : reader.GetValue(columnName);
            return ConvertValue<D>(value);
        }
        private D ConvertValue<D>(dynamic value)
        {
            Type vType = typeof(D);
            if (vType == typeof(string))
            {
                return Convert.ToString(value);
            }
            if (vType == typeof(int))
            {
                var _value = FixNumeric<int>(value);
                return Convert.ToInt32(value);
            }
            if (vType == typeof(short))
            {
                var _value = FixNumeric<short>(value);
                return Convert.ToInt16(_value);
            }
            if (vType == typeof(double))
            {
                return Convert.ToDouble(value);
            }
            if (vType == typeof(decimal))
            {
                return Convert.ToDecimal(value);
            }
            if (vType == typeof(DateTime))
            {
                return Convert.ToDateTime(value);
            }
            if (vType == typeof(bool))
            {
                return Convert.ToBoolean(value);
            }
            if (vType == typeof(char))
            {
                return Convert.ToChar(value);
            }
            if (vType == typeof(long))
            {
                var _value = FixNumeric<long>(value);
                return Convert.ToInt64(_value);
            }
            else
            {
                return Convert.ToString(value);
            }
        }
    }
}
