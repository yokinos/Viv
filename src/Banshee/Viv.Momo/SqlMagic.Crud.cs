using Dapper;
using Viv.Delusion;
using Viv.Momo.Enums;

namespace Viv.Momo
{
    /// <summary>
    /// 跨数据库 SQL 生成器（兼容 PostgreSQL / SQL Server）
    /// - 参数化版本：返回 (sql, DynamicParameters)，用于 Dapper 执行
    /// - Raw 版本：返回内联值 SQL 字符串，用于非参数化场景
    /// 列名/表名一律走 <see cref="MomoIdentifier"/>，与 EF / SchemaSynchronizer 同一套物理名。
    /// ignoreKeys / whereKeys 按 CLR 属性名匹配（大小写不敏感），不要拿加过引号的物理名去 Contains。
    /// </summary>
    public static partial class SqlMagic
    {
        #region 参数化版本

        public static (string sql, DynamicParameters parameters) CreateInsertSql(
            string tableName, object entity, DatabaseSourceType databaseSource, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                var paramName = $"@p{idx++}";
                fieldList.Add(MomoIdentifier.QuoteClr(property.Name, databaseSource));
                valueList.Add(paramName);
                parameters.Add(paramName, value);
            }

            var sql = $"INSERT INTO {QuoteTable(tableName, databaseSource)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
            return (sql, parameters);
        }

        public static (string sql, DynamicParameters parameters) CreateUpdateSql(
            string tableName, object entity, string whereKeys, DatabaseSourceType databaseSource, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var name = MomoIdentifier.QuoteClr(property.Name, databaseSource);
                var value = property.GetValue(entity);
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, value);

                if (whereKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase))
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

            var sql = $"UPDATE {QuoteTable(tableName, databaseSource)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
            return (sql, parameters);
        }

        public static (string sql, DynamicParameters parameters) CreateDeleteSql(
            string tableName, object entity, DatabaseSourceType databaseSource)
        {
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());
            int idx = 0;

            foreach (var property in propertieList)
            {
                var name = MomoIdentifier.QuoteClr(property.Name, databaseSource);
                var value = property.GetValue(entity);
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, value);

                var line = whereList.Count == 0 ? "WHERE" : "AND";
                whereList.Add($"{line} {name} = {paramName}");
            }

            var sql = $"DELETE FROM {QuoteTable(tableName, databaseSource)} {string.Join(" ", whereList)}";
            return (sql, parameters);
        }

        #endregion

        #region Raw 版本（内联值，非参数化）

        public static string CreateInsertSqlRaw(
            string tableName, object entity, DatabaseSourceType databaseSource, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                fieldList.Add(MomoIdentifier.QuoteClr(property.Name, databaseSource));
                valueList.Add(ToDatabaseValue(value, databaseSource));
            }

            return $"INSERT INTO {QuoteTable(tableName, databaseSource)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
        }

        public static string CreateUpdateSqlRaw(
            string tableName, object entity, string whereKeys, DatabaseSourceType databaseSource, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var name = MomoIdentifier.QuoteClr(property.Name, databaseSource);
                var value = property.GetValue(entity);
                var dbValue = ToDatabaseValue(value, databaseSource);

                if (whereKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase))
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

            return $"UPDATE {QuoteTable(tableName, databaseSource)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
        }

        public static string CreateDeleteSqlRaw(
            string tableName, object entity, DatabaseSourceType databaseSource)
        {
            var whereList = new List<string>();
            var propertieList = VivTypeReflectionCache.GetPropertieList(entity.GetType());

            foreach (var property in propertieList)
            {
                var name = MomoIdentifier.QuoteClr(property.Name, databaseSource);
                var value = property.GetValue(entity);

                var line = whereList.Count == 0 ? "WHERE" : "AND";
                whereList.Add($"{line} {name} = {ToDatabaseValue(value, databaseSource)}");
            }

            return $"DELETE FROM {QuoteTable(tableName, databaseSource)} {string.Join(" ", whereList)}";
        }

        #endregion

        /// <summary>
        /// 手写表名（测试里的 users、或已经 Quote 过的 GetTableName 结果）。
        /// 已带方括号的不再改写，避免 [[AtUser]]。
        /// </summary>
        private static string QuoteTable(string tableName, DatabaseSourceType databaseSource)
        {
            if (string.IsNullOrEmpty(tableName) || tableName[0] == '[') return tableName;
            return MomoIdentifier.QuoteClr(tableName, databaseSource);
        }
    }
}
