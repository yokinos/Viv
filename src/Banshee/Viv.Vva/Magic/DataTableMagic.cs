using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Viv.Vva.Extension;

namespace Viv.Vva.Magic
{
    public class DataTableMagic
    {
        [return: MaybeNull]
        public static List<T> ToList<T>([AllowNull] DataTable dt) where T : new()
        {
            if (dt.IsNullOrEmpty())
            {
                return default;
            }

            int rowCount = dt.Rows.Count;
            var result = new List<T>(rowCount);
            Type type = typeof(T);

            var fieldInfos = type.GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f);
            var propertyInfos = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).ToDictionary(p => p.Name, p => p);

            var setters = new List<Action<T, DataRow>>(dt.Columns.Count);
            for (int colIndex = 0; colIndex < dt.Columns.Count; colIndex++)
            {
                var colName = dt.Columns[colIndex].ColumnName;
                if (fieldInfos.TryGetValue(colName, out var fi))
                {
                    var memberType = fi.FieldType;
                    setters.Add((target, row) =>
                    {
                        if (row.IsNull(colIndex))
                        {
                            fi.SetValue(target, null);
                            return;
                        }

                        var raw = row[colIndex];
                        var converted = ConvertValue(raw, memberType);
                        fi.SetValue(target, converted);
                    });
                }
                else if (propertyInfos.TryGetValue(colName, out var pi) && pi.CanWrite)
                {
                    var memberType = pi.PropertyType;
                    setters.Add((target, row) =>
                    {
                        if (row.IsNull(colIndex))
                        {
                            pi.SetValue(target, null);
                            return;
                        }

                        var raw = row[colIndex];
                        var converted = ConvertValue(raw, memberType);
                        pi.SetValue(target, converted);
                    });
                }
                else
                {
                    // 列在目标类型中不存在，跳过（不分配 setter）
                }
            }

            foreach (DataRow row in dt.Rows)
            {
                var t = new T();
                foreach (var setter in setters)
                {
                    setter(t, row);
                }
                result.Add(t);
            }

            return result;
        }

        public static List<string> GetColumnNames(DataColumnCollection collection)
        {
            if (collection == null)
            {
                return [];
            }

            var fields = new List<string>(collection.Count);
            for (int i = 0; i < collection.Count; i++)
            {
                fields.Add(collection[i].ColumnName);
            }

            return fields;
        }

        [return: MaybeNull]
        private static object? ConvertValue([AllowNull] object? obj, Type targetType)
        {
            if (obj == null || obj is DBNull) return null;

            var sourceType = obj.GetType();
            if (targetType.IsAssignableFrom(sourceType))
            {
                return obj;
            }

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            try
            {
                if (underlying.IsEnum)
                {
                    if (obj is string s)
                    {
                        if (Enum.TryParse(underlying, s, true, out var enumParsed))
                            return enumParsed;
                    }

                    return Enum.ToObject(underlying, Convert.ChangeType(obj, Enum.GetUnderlyingType(underlying), CultureInfo.InvariantCulture));
                }

                if (underlying == typeof(Guid))
                {
                    if (obj is Guid g) return g;
                    if (obj is string gs && Guid.TryParse(gs, out var gp)) return gp;
                    return null;
                }

                if (underlying == typeof(DateTime))
                {
                    if (obj is DateTime dt) return dt;
                    if (obj is string sdt && DateTime.TryParse(sdt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDt)) return parsedDt;
                    if (long.TryParse(Convert.ToString(obj, CultureInfo.InvariantCulture), out var unix))
                    {
                        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        return unix > 1_000_000_000_000L ? epoch.AddMilliseconds(unix).ToLocalTime() : epoch.AddSeconds(unix).ToLocalTime();
                    }
                }

                if (underlying == typeof(DateTimeOffset))
                {
                    if (obj is DateTimeOffset dto) return dto;
                    if (obj is string sdto && DateTimeOffset.TryParse(sdto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDto)) return parsedDto;
                    if (long.TryParse(Convert.ToString(obj, CultureInfo.InvariantCulture), out var unix))
                    {
                        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        return unix > 1_000_000_000_000L ? epoch.AddMilliseconds(unix).ToLocalTime() : epoch.AddSeconds(unix).ToLocalTime();
                    }
                }

                if (underlying == typeof(byte[]))
                {
                    if (obj is byte[] ba) return ba;
                    if (obj is string s) return Encoding.UTF8.GetBytes(s);
                }
                return Convert.ChangeType(obj, underlying, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        [return: MaybeNull]
        public static DataTable ToDataTable<T>([AllowNull] IList<T> list)
        {
            if (list == null)
            {
                return default;
            }

            DataTable result = new DataTable();
            Type type = typeof(T);

            PropertyInfo[] propertys = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (PropertyInfo prop in propertys)
            {
                result.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            }

            int length = propertys.Length;
            var values = new object[length];
            foreach (T obj in list)
            {
                Array.Clear(values, 0, length);
                for (int i = 0; i < length; i++)
                {
                    var value = propertys[i].GetValue(obj, null);
                    if (value != null)
                    {
                        values[i] = value;
                    }
                }

                result.Rows.Add(values);
            }

            return result;
        }
    }
}