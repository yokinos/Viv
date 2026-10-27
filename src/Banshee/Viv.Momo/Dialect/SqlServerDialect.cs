using Dapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Momo;
using Viv.Momo.Converter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.SqlServer;

namespace Viv.Momo.Dialect
{
    /// <summary>
    /// SQL Server 方言：PascalCase 命名、<c>[x]</c> 引用符、<c>OFFSET/FETCH</c> 分页、
    /// <c>GETDATE()</c> 当前时间、布尔字面量 <c>1/0</c>。
    ///
    /// 自包含：不继承基类、不依赖任何静态 SQL 工具 —— 改这个方言只碰这个文件。
    /// </summary>
    public sealed class SqlServerDialect : ISqlDialect
    {
        static SqlServerDialect()
        {
            // PostgreSQL 那条也依赖这个全局设置（物理列 snake_case 映射回属性名），
            // 两个方言谁先被创建都会打开它；SQL Server 不受影响。
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        /// <inheritdoc />
        public DatabaseSourceType Source => DatabaseSourceType.SqlServer;

        /// <summary>实体类型 → (表名, 是否来自显式 [Table])，反射结果缓存</summary>
        private static readonly ConcurrentDictionary<Type, (string Name, bool Explicit)> _tableNameCache = [];

        #region 命名与引用符

        /// <inheritdoc />
        public string ToPhysical(string clrName) => clrName;

        /// <inheritdoc />
        public string Quote(string physicalName) => $"[{physicalName}]";

        /// <inheritdoc />
        public string QuoteClr(string clrName) => Quote(ToPhysical(clrName));

        /// <inheritdoc />
        public string ToSnakeCase(string name) => name;

        /// <inheritdoc />
        public string GetTableName<T>()
        {
            var entityType = typeof(T);
            if (!_tableNameCache.TryGetValue(entityType, out var cached))
            {
                var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
                cached = (tableAttr?.Name ?? entityType.Name, tableAttr != null);
                _tableNameCache[entityType] = cached;
            }

            return Quote(ToPhysical(cached.Name));
        }

        /// <inheritdoc />
        public string QuoteIdentifier(string field) => QuoteClr(field);

        #endregion

        #region 模板

        /// <inheritdoc />
        public string GetInsertSqlTemplate(string tableName, Type type)
        {
            var propertyNameList = VivTypeReflectionCache.GetPropertyNameList(type);
            var quotedColumns = propertyNameList.Select(QuoteIdentifier).ToList();

            // 参数占位符必须用原始属性名：引号后的 [Name] 生成 @[Name] 会绑不上参数
            return $"INSERT INTO {tableName}({string.Join(",", quotedColumns)}) VALUES({string.Join(",", propertyNameList.Select(x => $"@{x}"))})";
        }

        /// <inheritdoc />
        public string GetFindSqlTemplate(string tableName)
            => $"SELECT * FROM {tableName} WHERE {QuoteIdentifier("Id")} = @Id";

        /// <inheritdoc />
        public string GetFindSqlTemplate(
            string tableName,
            long tenantId,
            IReadOnlyList<IMomoDataFilter> filters,
            IDictionary<string, object> parameters)
        {
            var sql = GetFindSqlTemplate(tableName);
            foreach (var filter in filters)
            {
                sql += filter.BuildSqlCondition(tenantId, this, parameters);
            }

            return sql;
        }

        /// <inheritdoc />
        public (string PageSql, string CountSql) GetPageSqlTemplate(string sql, int pageIndex, int pageSize)
        {
            var analysis = new MssqlSqlAnalyzer().Analyze(sql);
            return (analysis.BuildPageSql(pageIndex, pageSize), analysis.BuildCountSql());
        }

        #endregion

        #region 条件删除

        /// <inheritdoc />
        public (string Sql, Dictionary<string, object> Parameter) GetDeleteSql<T>(
            string tableName,
            Expression<Func<T, bool>> expression,
            long tenantId = 0)
        {
            if (expression == null)
                return (string.Empty, []);

            var (where, parameters) = new ExpressionToSqlConverter(this).Convert(expression);
            AppendTenantFilter(ref where, parameters, typeof(T), tenantId);
            return ($"DELETE FROM {tableName} WHERE {where}", parameters);
        }

        /// <inheritdoc />
        public (string Sql, Dictionary<string, object> Parameter) GetSoftDeleteSql<T>(
            string tableName,
            Expression<Func<T, bool>> expression,
            long tenantId = 0) where T : IEntity, ISoftDeleted
        {
            if (expression == null)
                return (string.Empty, []);

            var isDeletedCol = QuoteIdentifier(nameof(ISoftDeleted.IsDeleted));
            var deletedAtCol = QuoteIdentifier(nameof(ISoftDeleted.DeletedAt));

            var (whereSql, parameters) = new ExpressionToSqlConverter(this).Convert(expression);
            AppendTenantFilter(ref whereSql, parameters, typeof(T), tenantId);

            return ($"UPDATE {tableName} SET {isDeletedCol} = 1, {deletedAtCol} = GETDATE() WHERE {whereSql}", parameters);
        }

        private void AppendTenantFilter(ref string where, Dictionary<string, object> parameters, Type entityType, long tenantId)
        {
            if (tenantId > 0 && typeof(ITenant).IsAssignableFrom(entityType))
            {
                where += $" AND {QuoteIdentifier("TenantId")} = @TenantId";
                parameters["TenantId"] = tenantId;
            }
        }

        #endregion

        #region 字面量

        /// <inheritdoc />
        public string ToDatabaseValue(object? value)
        {
            if (value == null) return "NULL";

            var realType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

            return realType.Name switch
            {
                nameof(Int32) or nameof(Int64) or nameof(Decimal) or nameof(Byte) or nameof(SByte) or
                nameof(Double) or nameof(Single) or nameof(Int16) or nameof(UInt32) or nameof(UInt64) or
                nameof(UInt16) => value.ToString()!,

                nameof(Boolean) => (bool)value ? "1" : "0",

                nameof(String) => $"'{EscapeSqlQuote(value.ToString()!)}'",

                nameof(DateTime) => $"'{FormatDateTime((DateTime)value)}'",
                nameof(DateTimeOffset) => $"'{FormatDateTime(((DateTimeOffset)value).UtcDateTime)}'",

                _ when realType.IsEnum => ((int)value).ToString(),

                _ => $"'{EscapeSqlQuote(value.ToJson()!)}'"
            };
        }

        private static string EscapeSqlQuote(string input) => input.Replace("'", "''");

        private static string FormatDateTime(DateTime dt)
            => dt.TimeOfDay == TimeSpan.Zero ? dt.ExtToString(DateFormat.Date) : dt.ExtToString();

        #endregion

        #region CRUD 语句（拼装共享在 SqlMagic，这里只转发 —— 只有命名/字面量因方言而异）

        /// <inheritdoc />
        public (string Sql, DynamicParameters Parameters) CreateInsertSql(string tableName, object entity, string ignoreKeys = "")
            => SqlMagic.CreateInsertSql(this, tableName, entity, ignoreKeys);

        /// <inheritdoc />
        public (string Sql, DynamicParameters Parameters) CreateUpdateSql(string tableName, object entity, string whereKeys, string ignoreKeys = "")
            => SqlMagic.CreateUpdateSql(this, tableName, entity, whereKeys, ignoreKeys);

        /// <inheritdoc />
        public (string Sql, DynamicParameters Parameters) CreateDeleteSql(string tableName, object entity)
            => SqlMagic.CreateDeleteSql(this, tableName, entity);

        /// <inheritdoc />
        public string CreateInsertSqlRaw(string tableName, object entity, string ignoreKeys = "")
            => SqlMagic.CreateInsertSqlRaw(this, tableName, entity, ignoreKeys);

        /// <inheritdoc />
        public string CreateUpdateSqlRaw(string tableName, object entity, string whereKeys, string ignoreKeys = "")
            => SqlMagic.CreateUpdateSqlRaw(this, tableName, entity, whereKeys, ignoreKeys);

        /// <inheritdoc />
        public string CreateDeleteSqlRaw(string tableName, object entity)
            => SqlMagic.CreateDeleteSqlRaw(this, tableName, entity);

        #endregion
    }
}