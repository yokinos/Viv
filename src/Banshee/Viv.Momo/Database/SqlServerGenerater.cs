using Azure;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Momo.Database
{
    public class SqlServerGenerater : ISqlGenerater
    {
        public string CreateInsertSql(string tableName, object entity, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            foreach (var property in propertieList)
            {
                var name = property.Name;
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                fieldList.Add(name);
                valueList.Add(ToDatabaseValue(value));
            }

            var sql = $"INSERT INTO {tableName} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
            return sql;
        }

        public string CreateUpdateSql(string tableName, object entity, string whereKeys, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();

            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            foreach (var property in propertieList)
            {
                var name = property.Name;
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                var databaseValue = ToDatabaseValue(value);
                if (whereKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    var line = whereList.Count == 0 ? "WHERE" : " AND";
                    whereList.Add($"{line} {name} = {databaseValue}");
                }
                else
                {
                    setList.Add($"{name} = {databaseValue}");
                }
            }

            if (whereList.Count == 0) throw new ArgumentException("WhereKeys is empty.");

            var sql = $"UPDATE {tableName} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
            return sql;
        }

        public string CreateDeleteSql(string tableName, object entity)
        {
            var whereList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            foreach (var property in propertieList)
            {
                var name = property.Name;
                var value = property.GetValue(entity);
                var databaseValue = ToDatabaseValue(value);
                var line = whereList.Count == 0 ? "WHERE" : " AND";
                whereList.Add($"{line} {name} = {databaseValue}");
            }

            var sql = $"DELETE FROM {tableName} {string.Join(" ", whereList)}";
            return sql;
        }

        [return: NotNull]
        public string ToDatabaseValue([AllowNull] object value)
        {
            if (value == null) return "NULL";
            var valueType = value.GetType();
            var underlyingType = Nullable.GetUnderlyingType(valueType);
            var realType = underlyingType ?? valueType;

            return realType.Name switch
            {
                // 数字类型：直接返回值
                nameof(Int32) or nameof(Int64) or nameof(Decimal) or nameof(Byte) or nameof(SByte) or nameof(Double) or 
                nameof(Single) or nameof(Int16) or nameof(UInt32) or nameof(UInt64) or nameof(UInt16) => value.ToString()!,
                // 布尔类型：PG的布尔值是 true/false（小写）
                nameof(Boolean) => (bool)value ? "1" : "0",
                // 字符串类型：加单引号 + 转义单引号（防注入）
                nameof(String) => $"'{EscapeSingleQuote(value.ToString()!)}'",
                // 日期时间类型：格式化为PG兼容的ISO格式 + 单引号
                nameof(DateTime) => $"'{DateTimeConver((DateTime)value)}'",
                nameof(DateTimeOffset) => $"'{DateTimeOffsetConver(((DateTimeOffset)value).UtcDateTime)}'",
                // 枚举类型：取枚举值（数字）或名称（根据业务调整）
                _ when realType.IsEnum => ((int)value).ToString(),
                // 其他类型：转为JSON字符串
                _ => $"'{EscapeSingleQuote(value.ToJson()!)}'"
            };
        }

        private static string EscapeSingleQuote(string input)
        {
            return input.Replace("'", "''");
        }

        private static string DateTimeConver(DateTime dateTime)
        {
            if (dateTime.TimeOfDay == TimeSpan.Zero)
            {
                return dateTime.ExtToString(DateFormat.DateOnly);
            }

            return dateTime.ExtToString();
        }

        private static string DateTimeOffsetConver(DateTime dateTime)
        {
            return DateTimeConver(dateTime);
        }
    }
}
