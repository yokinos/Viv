using Dapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Momo;
using Viv.Momo.Converter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Dialect
{
    /// <summary>
    /// PostgreSQL 方言：snake_case 命名、标识符不加引用符、<c>LIMIT/OFFSET</c> 分页、
    /// <c>NOW()</c> 当前时间、布尔字面量 <c>true/false</c>。
    ///
    /// 自包含：不继承基类、不依赖任何静态 SQL 工具 —— 改这个方言只碰这个文件。
    /// </summary>
    public sealed class PostgresDialect : ISqlDialect
    {
        static PostgresDialect()
        {
            // PostgreSQL 物理列是 snake_case，Dapper 默认只按属性名精确匹配（忽略大小写）。
            // 打开之后 display_name → DisplayName；SQL Server 返回 DisplayName 仍然匹配。
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        /// <inheritdoc />
        public DatabaseSourceType Source => DatabaseSourceType.PostgreSQL;

        /// <summary>实体类型 → (表名, 是否来自显式 [Table])，反射结果缓存</summary>
        private static readonly ConcurrentDictionary<Type, (string Name, bool Explicit)> _tableNameCache = [];

        #region 命名与引用符

        /// <inheritdoc />
        public string ToPhysical(string clrName)
            => string.IsNullOrEmpty(clrName) ? clrName : ToSnakeCase(clrName);

        /// <inheritdoc />
        public string Quote(string physicalName) => physicalName;

        /// <inheritdoc />
        public string QuoteClr(string clrName) => Quote(ToPhysical(clrName));

        /// <inheritdoc />
        public string ToSnakeCase(string name) => SnakeCase(name);

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

            // 显式 [Table] 的名字已经是物理名：不再改写（与 SQL Server 一致）
            return Quote(cached.Explicit ? cached.Name : ToPhysical(cached.Name));
        }

        /// <inheritdoc />
        public string QuoteIdentifier(string field) => QuoteClr(field);

        /// <summary>
        /// 与 EFCore.NamingConventions 10.x <c>SnakeCaseNameRewriter</c> 同一套规则（InvariantCulture）。
        /// 钉住它是为了 EF 建表名和 Dapper 拼出来的标识符字节级一致。
        /// </summary>
        private static string SnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var builder = new StringBuilder(name.Length + Math.Min(2, name.Length / 5));
            UnicodeCategory? previousCategory = null;

            for (var currentIndex = 0; currentIndex < name.Length; currentIndex++)
            {
                var currentChar = name[currentIndex];
                if (currentChar == '_')
                {
                    builder.Append('_');
                    previousCategory = null;
                    continue;
                }

                var currentCategory = char.GetUnicodeCategory(currentChar);
                switch (currentCategory)
                {
                    case UnicodeCategory.UppercaseLetter:
                    case UnicodeCategory.TitlecaseLetter:
                        if (previousCategory == UnicodeCategory.SpaceSeparator ||
                            previousCategory == UnicodeCategory.LowercaseLetter ||
                            previousCategory != UnicodeCategory.DecimalDigitNumber &&
                            previousCategory != null &&
                            currentIndex > 0 &&
                            currentIndex + 1 < name.Length &&
                            char.IsLower(name[currentIndex + 1]))
                        {
                            builder.Append('_');
                        }

                        currentChar = char.ToLower(currentChar, CultureInfo.InvariantCulture);
                        break;

                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.DecimalDigitNumber:
                        if (previousCategory == UnicodeCategory.SpaceSeparator)
                        {
                            builder.Append('_');
                        }
                        break;

                    default:
                        if (previousCategory != null)
                        {
                            previousCategory = UnicodeCategory.SpaceSeparator;
                        }
                        continue;
                }

                builder.Append(currentChar);
                previousCategory = currentCategory;
            }

            return builder.ToString();
        }

        #endregion

        #region 模板

        /// <inheritdoc />
        public string GetInsertSqlTemplate(string tableName, Type type)
        {
            var propertyNameList = VivTypeReflectionCache.GetPropertyNameList(type);
            var quotedColumns = propertyNameList.Select(QuoteIdentifier).ToList();

            // 参数占位符必须用原始属性名（snake_case 列名不参与参数名）
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
            var offset = (pageIndex - 1) * pageSize;
            var countSql = $"SELECT COUNT(*) FROM ({RemoveOrderBy(sql)}) AS t";
            return ($"{sql} LIMIT {pageSize} OFFSET {offset}", countSql);
        }

        private static string RemoveOrderBy(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return sql;

            var lastIndex = sql.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
            if (lastIndex < 0) return sql;

            // 确保不是子查询中的 ORDER BY（后面不能再有右括号）
            if (sql[lastIndex..].IndexOf(')') >= 0) return sql;

            return sql[..lastIndex].TrimEnd();
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

            return ($"UPDATE {tableName} SET {isDeletedCol} = true, {deletedAtCol} = NOW() WHERE {whereSql}", parameters);
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

                nameof(Boolean) => (bool)value ? "true" : "false",

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