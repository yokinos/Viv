using System.Data;
using Dapper;
using Viv.Momo.Enums;
using Viv.Vva;

namespace Viv.Momo
{
    /// <summary>
    /// 跨数据库 SQL 生成器（兼容 PostgreSQL / SQL Server）
    /// - 参数化版本：返回 (sql, DynamicParameters)，用于 Dapper 执行
    /// - Raw 版本：返回内联值 SQL 字符串，用于非参数化场景
    /// </summary>
    public static class CrudMagic
    {
        #region 参数化版本

        public static (string sql, DynamicParameters parameters) CreateInsertSql(
            string tableName, object entity, DatabaseSouceType databaseType, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                var paramName = $"@p{idx++}";
                fieldList.Add(name);
                valueList.Add(paramName);
                parameters.Add(paramName, value);
            }

            var sql = $"INSERT INTO {FormatName(tableName, databaseType)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
            return (sql, parameters);
        }

        public static (string sql, DynamicParameters parameters) CreateUpdateSql(
            string tableName, object entity, string whereKeys, DatabaseSouceType databaseType, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, value);

                if (whereKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    var line = whereList.Count == 0 ? "WHERE" : "AND";
                    whereList.Add($"{line} {name} = {paramName}");
                }
                else
                {
                    setList.Add($"{name} = {paramName}");
                }
            }

            if (whereList.Count == 0) throw new ArgumentException("WhereKeys is empty.");

            var sql = $"UPDATE {FormatName(tableName, databaseType)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
            return (sql, parameters);
        }

        public static (string sql, DynamicParameters parameters) CreateDeleteSql(
            string tableName, object entity, DatabaseSouceType databaseType)
        {
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                var value = property.GetValue(entity);
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, value);

                var line = whereList.Count == 0 ? "WHERE" : "AND";
                whereList.Add($"{line} {name} = {paramName}");
            }

            var sql = $"DELETE FROM {FormatName(tableName, databaseType)} {string.Join(" ", whereList)}";
            return (sql, parameters);
        }

        #endregion

        #region Raw 版本（内联值，非参数化）

        public static string CreateInsertSqlRaw(
            string tableName, object entity, DatabaseSouceType databaseType, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                fieldList.Add(name);
                valueList.Add(SqlMagic.ToDatabaseValue(value, databaseType));
            }

            return $"INSERT INTO {FormatName(tableName, databaseType)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
        }

        public static string CreateUpdateSqlRaw(
            string tableName, object entity, string whereKeys, DatabaseSouceType databaseType, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                if (ignoreKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                var dbValue = SqlMagic.ToDatabaseValue(value, databaseType);

                if (whereKeys.Contains(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    var line = whereList.Count == 0 ? "WHERE" : "AND";
                    whereList.Add($"{line} {name} = {dbValue}");
                }
                else
                {
                    setList.Add($"{name} = {dbValue}");
                }
            }

            if (whereList.Count == 0) throw new ArgumentException("WhereKeys is empty.");

            return $"UPDATE {FormatName(tableName, databaseType)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
        }

        public static string CreateDeleteSqlRaw(
            string tableName, object entity, DatabaseSouceType databaseType)
        {
            var whereList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                var name = FormatName(property.Name, databaseType);
                var value = property.GetValue(entity);

                var line = whereList.Count == 0 ? "WHERE" : "AND";
                whereList.Add($"{line} {name} = {SqlMagic.ToDatabaseValue(value, databaseType)}");
            }

            return $"DELETE FROM {FormatName(tableName, databaseType)} {string.Join(" ", whereList)}";
        }

        #endregion

        private static string FormatName(string name, DatabaseSouceType databaseType)
        {
            return databaseType switch
            {
                DatabaseSouceType.PostgreSQL => name.ToLowerInvariant(),
                _ => name
            };
        }
    }
}
