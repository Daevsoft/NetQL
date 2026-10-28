#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Daevsoft.Lib
{
    public class ReaderUtil<T>
    {
        private readonly IDataReader reader;
        // A wrapper belongs to one result set; create a new one after NextResult().
        private Dictionary<string, int>? ordinals;

        public ReaderUtil(DbDataReader reader) : this((IDataReader)reader)
        {
        }

        public ReaderUtil(IDataReader reader)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public bool IsColumnExist(string columnName)
        {
            return columnName != null && GetOrdinals().ContainsKey(columnName);
        }

        public bool TryGetOrdinal(string columnName, out int ordinal)
        {
            if (columnName != null) return GetOrdinals().TryGetValue(columnName, out ordinal);
            ordinal = -1;
            return false;
        }

        private Dictionary<string, int> GetOrdinals()
        {
            if (ordinals == null)
            {
                var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    // Named access selects the first match; ordinal access stays unambiguous.
                    if (!lookup.ContainsKey(name))
                        lookup.Add(name, i);
                }
                ordinals = lookup;
            }
            return ordinals;
        }

        public D GetValue<D>(int ordinal)
        {
            return (D)ConvertValue(reader.GetValue(ordinal), typeof(D))!;
        }

        public D GetValue<D>(string columnName)
        {
            if (columnName == null)
                throw new ArgumentNullException(nameof(columnName));
            if (!GetOrdinals().TryGetValue(columnName, out var ordinal))
                throw new IndexOutOfRangeException($"Column '{columnName}' was not found.");
            return GetValue<D>(ordinal);
        }

        internal static object? ConvertValue(object? value, Type targetType)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (value == null || value == DBNull.Value)
                return nullableType != null || !targetType.IsValueType
                    ? null : Activator.CreateInstance(targetType);

            targetType = nullableType ?? targetType;
            if (targetType.IsInstanceOfType(value))
                return value;

            if (targetType.IsEnum)
            {
                if (value is string name)
                    return Enum.Parse(targetType, name, true);
                return Enum.ToObject(targetType,
                    Convert.ChangeType(value, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture));
            }
            if (targetType == typeof(Guid))
            {
                if (value is string guid)
                    return Guid.Parse(guid);
                if (value is byte[] bytes)
                    return new Guid(bytes);
            }
            if (targetType == typeof(DateTimeOffset))
            {
                if (value is DateTime dateTime)
                    return new DateTimeOffset(dateTime);
                if (value is string dateTimeOffset)
                    return DateTimeOffset.Parse(dateTimeOffset, CultureInfo.InvariantCulture);
            }
            if (targetType == typeof(TimeSpan) && value is string timeSpan)
                return TimeSpan.Parse(timeSpan, CultureInfo.InvariantCulture);

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
