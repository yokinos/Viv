using System;
using System.Collections.Generic;

using Dapper;

using Viv.Delusion;
using Viv.Momo.Dialect;
using Viv.Momo.Enums;

namespace Viv.Momo
{
    /// <summary>
    /// SQL 生成的总入口。
    ///
    /// 分工：
    /// <list type="bullet">
    /// <item><description><see cref="For"/> 按库类型取方言实例（<see cref="ISqlDialect"/> 的实现），
    /// 方言负责**真正因库而异**的部分：命名、引用符、分页、当前时间、布尔字面量。</description></item>
    /// <item><description>下面的 CRUD 方法负责"把实体属性列表拼成 INSERT/UPDATE/DELETE"，
    /// 唯一的方言接触点是 <c>dialect.QuoteClr</c> 与 <c>dialect.ToDatabaseValue</c>。</description></item>
    /// </list>
    ///
    /// 拼装放在这里而不是两个方言各写一遍：它是纯遍历 + 字符串拼接，没有任何方言差异。
    ///
    /// ⚠️ 参数名前缀固定 `@p{n}`；`ignoreKeys` / `whereKeys` 按 CLR 属性名匹配（大小写不敏感），
    /// 不要拿加过引号的物理名去 Contains。
    /// </summary>
    public static class SqlMagic
    {
        #region 方言实例

        /// <summary>方言实例无状态，进程内复用</summary>
        private static readonly ISqlDialect SqlServer = new SqlServerDialect();

        private static readonly ISqlDialect Postgres = new PostgresDialect();

        /// <summary>
        /// 按库类型取方言。
        ///
        /// 调用方优先用 <c>IMomoDbContext.Dialect</c>（跟连接同源）；这里留给拿不到连接的场景
        /// （SchemaSynchronizer 按配置建、测试）。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">未支持的库类型（配置写错应当启动即炸，不静默回落）</exception>
        public static ISqlDialect For(DatabaseSourceType source) => source switch
        {
            DatabaseSourceType.PostgreSQL => Postgres,
            DatabaseSourceType.SqlServer => SqlServer,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "未支持的数据库类型")
        };

        #endregion

        #region 参数化版本

        public static (string Sql, DynamicParameters Parameters) CreateInsertSql(
            ISqlDialect dialect, string tableName, object entity, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();
            var parameters = new DynamicParameters();
            var idx = 0;

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                var paramName = $"@p{idx++}";
                fieldList.Add(dialect.QuoteClr(property.Name));
                valueList.Add(paramName);
                parameters.Add(paramName, value);
            }

            return ($"INSERT INTO {QuoteTable(dialect, tableName)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})", parameters);
        }

        public static (string Sql, DynamicParameters Parameters) CreateUpdateSql(
            ISqlDialect dialect, string tableName, object entity, string whereKeys, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var idx = 0;

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var name = dialect.QuoteClr(property.Name);
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, property.GetValue(entity));

                if (whereKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    whereList.Add($"{(whereList.Count == 0 ? "WHERE" : "AND")} {name} = {paramName}");
                }
                else
                {
                    setList.Add($"{name} = {paramName}");
                }
            }

            if (whereList.Count == 0) throw new ArgumentException("WhereKeys is empty.");

            return ($"UPDATE {QuoteTable(dialect, tableName)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}", parameters);
        }

        public static (string Sql, DynamicParameters Parameters) CreateDeleteSql(
            ISqlDialect dialect, string tableName, object entity)
        {
            var whereList = new List<string>();
            var parameters = new DynamicParameters();
            var idx = 0;

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                var paramName = $"@p{idx++}";
                parameters.Add(paramName, property.GetValue(entity));
                whereList.Add($"{(whereList.Count == 0 ? "WHERE" : "AND")} {dialect.QuoteClr(property.Name)} = {paramName}");
            }

            return ($"DELETE FROM {QuoteTable(dialect, tableName)} {string.Join(" ", whereList)}", parameters);
        }

        #endregion

        #region Raw 版本（内联值，非参数化）

        public static string CreateInsertSqlRaw(
            ISqlDialect dialect, string tableName, object entity, string ignoreKeys = "")
        {
            var fieldList = new List<string>();
            var valueList = new List<string>();

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var value = property.GetValue(entity);
                if (value == null) continue;

                fieldList.Add(dialect.QuoteClr(property.Name));
                valueList.Add(dialect.ToDatabaseValue(value));
            }

            return $"INSERT INTO {QuoteTable(dialect, tableName)} ({string.Join(",", fieldList)}) VALUES ({string.Join(",", valueList)})";
        }

        public static string CreateUpdateSqlRaw(
            ISqlDialect dialect, string tableName, object entity, string whereKeys, string ignoreKeys = "")
        {
            var setList = new List<string>();
            var whereList = new List<string>();

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                if (ignoreKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                var name = dialect.QuoteClr(property.Name);
                var dbValue = dialect.ToDatabaseValue(property.GetValue(entity));

                if (whereKeys.Contains(property.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    whereList.Add($"{(whereList.Count == 0 ? "WHERE" : "AND")} {name} = {dbValue}");
                }
                else
                {
                    setList.Add($"{name} = {dbValue}");
                }
            }

            if (whereList.Count == 0) throw new ArgumentException("WhereKeys is empty.");

            return $"UPDATE {QuoteTable(dialect, tableName)} SET {string.Join(",", setList)} {string.Join(" ", whereList)}";
        }

        public static string CreateDeleteSqlRaw(ISqlDialect dialect, string tableName, object entity)
        {
            var whereList = new List<string>();

            foreach (var property in VivTypeReflectionCache.GetPropertieList(entity.GetType()))
            {
                whereList.Add($"{(whereList.Count == 0 ? "WHERE" : "AND")} {dialect.QuoteClr(property.Name)} = {dialect.ToDatabaseValue(property.GetValue(entity))}");
            }

            return $"DELETE FROM {QuoteTable(dialect, tableName)} {string.Join(" ", whereList)}";
        }

        #endregion

        /// <summary>
        /// 手写表名（测试里的 users、或已经 Quote 过的 GetTableName 结果）。
        /// 已带方括号的不再改写，避免 [[AtUser]]。
        /// </summary>
        private static string QuoteTable(ISqlDialect dialect, string tableName)
        {
            if (string.IsNullOrEmpty(tableName) || tableName[0] == '[') return tableName;
            return dialect.QuoteClr(tableName);
        }
    }
}
