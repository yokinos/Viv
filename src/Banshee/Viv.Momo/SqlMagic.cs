using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Momo.Converter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.SqlServer;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Momo
{
    /// <summary>
    /// 通用数据操作工具类
    /// 包含：表名获取、查询条件拼接、SQL模板生成（插入/查询/分页）等核心方法
    /// </summary>
    public static partial class SqlMagic
    {
        /// <summary>
        /// 缓存实体类型对应的数据库表名称
        /// </summary>
        private static readonly ConcurrentDictionary<Type, string> _tableNameCache = [];

        /// <summary>
        /// 获取实体对应的数据库表名称（带缓存，避免重复反射）
        /// 优先读取实体类上的[Table]特性指定的名称，无特性则使用实体类名
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <returns>数据库表名称</returns>
        public static string GetTableName<T>(DatabaseSouceType databaseSouceType)
        {
            var entityType = typeof(T);
            if (!_tableNameCache.TryGetValue(entityType, out var tableName))
            {
                var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
                tableName = tableAttr?.Name ?? entityType.Name;
                _tableNameCache[entityType] = tableName;
            }

            return QuoteIdentifier(tableName, databaseSouceType);
        }

        public static string QuoteIdentifier(string field, DatabaseSouceType databaseSouceType)
        {
            return databaseSouceType switch
            {
                DatabaseSouceType.SqlServer => $"[{field}]",
                DatabaseSouceType.PostgreSQL => $"{field.ToLowerInvariant()}",
                _ => field
            };
        }

        /// <summary>
        /// 生成通用INSERT SQL模板
        /// 适配PostgreSQL（字段/表名小写）和其他数据库（如SQL Server）
        /// </summary>
        /// <param name="tableName">数据库表名称</param>
        /// <param name="type">实体类型（用于反射获取字段名）</param>
        /// <param name="databaseSouceType">数据库类型（PostgreSQL/SQL Server等）</param>
        /// <returns>带参数占位符的INSERT SQL语句</returns>
        public static string GetInsertSqlTemplate(string tableName, Type type, DatabaseSouceType databaseSouceType)
        {
            var propertyNameList = VivTypeReflectionCache.GetPropertyNameList(type);
            var nameListLower = propertyNameList.Select(x => QuoteIdentifier(x, databaseSouceType)).ToList();
            var sql = $"INSERT INTO {tableName}({string.Join(",", nameListLower)}) VALUES({string.Join(",", nameListLower.Select(x => $"@{x}"))})";
            return sql;
        }

        /// <summary>
        /// 生成根据ID查询单条数据的SELECT SQL模板
        /// 适配PostgreSQL（表名小写）和SQL Server（表名加方括号）
        /// </summary>
        /// <param name="tableName">数据库表名称</param>
        /// <param name="databaseSouceType">数据库类型（PostgreSQL/SQL Server等）</param>
        /// <returns>带参数占位符@Id的SELECT SQL语句</returns>
        public static string GetFindSqlTemplate(string tableName, DatabaseSouceType databaseSouceType)
        {
            return $"SELECT * FROM {tableName} WHERE {QuoteIdentifier("Id", databaseSouceType)} = @Id";
        }

        /// <summary>
        /// 生成分页查询SQL模板（总数统计+分页数据）
        /// 适配PostgreSQL（LIMIT/OFFSET）和SQL Server（OFFSET/FETCH）
        /// </summary>
        /// <param name="sql">原始查询SQL（如：SELECT * FROM table WHERE ...）</param>
        /// <param name="pageIndex">页码（从1开始）</param>
        /// <param name="pageSize">每页条数</param>
        /// <param name="databaseSouceType">数据库类型（PostgreSQL/SQL Server等）</param>
        /// <returns>分页查询SQL + 总数统计SQL</returns>
        public static (string pageSql, string countSql) GetPageSqlTemplate(string sql, int pageIndex, int pageSize, DatabaseSouceType databaseSouceType)
        {
            int offset = (pageIndex - 1) * pageSize;
            var sqlWithoutOrderBy = RemoveOrderBy(sql);
            var countSql = $"SELECT COUNT(*) FROM ({sqlWithoutOrderBy}) AS t";

            if (databaseSouceType == DatabaseSouceType.PostgreSQL)
            {
                var pageSql = $"{sql} LIMIT {pageSize} OFFSET {offset}";
                return (pageSql, countSql);
            }

            if (databaseSouceType == DatabaseSouceType.SqlServer)
            {
                var analyzer = new MssqlSqlAnalyzer();
                var analysis = analyzer.Analyze(sql);
                return (
                    MssqlPagingSqlBuilder.BuildPageSql(analysis, pageIndex, pageSize),
                    MssqlPagingSqlBuilder.BuildCountSql(analysis)
                );
            }

            throw new NotSupportedException($"Unsupported database type: {databaseSouceType}");
        }

        private static string RemoveOrderBy(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return sql;

            var lastIndex = sql.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
            if (lastIndex < 0) return sql;

            // 确保不是子查询中的 ORDER BY（后面不能再有右括号）
            var afterOrderBy = sql.Substring(lastIndex);
            var closeParenIndex = afterOrderBy.IndexOf(')');
            if (closeParenIndex >= 0) return sql;

            return sql.Substring(0, lastIndex).TrimEnd();
        }

        /// <summary>
        /// 生成真删除sql（物理删除）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="expression"></param>
        /// <param name="databaseSouceType"></param>
        /// <returns></returns>
        public static (string sql, Dictionary<string, object> parameter) GetDeleteSql<T>(
          string tableName,
          Expression<Func<T, bool>> expression,
          DatabaseSouceType databaseSouceType)
        {
            if (expression == null)
                return (string.Empty, []);

            var (where, parameters) = ExpressionToSqlConverter.Convert(expression, databaseSouceType);
            var sql = $"DELETE FROM {tableName} WHERE {where}";
            return (sql, parameters);
        }

        /// <summary>
        /// 生成软删除 SQL（更新 IsDeleted + DeletedAt）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="tableName">表名</param>
        /// <param name="expression">过滤条件</param>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>SQL + 参数</returns>
        public static (string sql, Dictionary<string, object> parameter) GetSoftDeleteSql<T>(
            string tableName,
            Expression<Func<T, bool>> expression,
            DatabaseSouceType databaseType) where T : IEntity, ISoftDelete
        {
            if (expression == null)
                return (string.Empty, []);

            var isDeletedCol = QuoteIdentifier(nameof(ISoftDelete.IsDeleted), databaseType);
            var deletedAtCol = QuoteIdentifier(nameof(ISoftDelete.DeletedAt), databaseType);

            string dateValue, boolValue;
            switch (databaseType)
            {
                case DatabaseSouceType.PostgreSQL:
                    dateValue = "NOW()";
                    boolValue = "true";
                    break;
                case DatabaseSouceType.SqlServer:
                    dateValue = "GETDATE()";
                    boolValue = "1";
                    break;
                default:
                    return (string.Empty, []);
            }

            var (whereSql, parameters) = ExpressionToSqlConverter.Convert(expression, databaseType);
            var sql = $"UPDATE {tableName} SET {isDeletedCol} = {boolValue}, {deletedAtCol} = {dateValue} WHERE {whereSql}";
            return (sql, parameters);
        }

        /// <summary>
        /// 将 CLR 值转换为数据库兼容的 SQL 字面量（兼容 PostgreSQL 和 SQL Server）
        /// 字符串会做单引号转义，非参数化场景使用
        /// </summary>
        public static string ToDatabaseValue(object? value, DatabaseSouceType databaseSouceType)
        {
            if (value == null) return "NULL";

            var valueType = value.GetType();
            var underlyingType = Nullable.GetUnderlyingType(valueType);
            var realType = underlyingType ?? valueType;

            return realType.Name switch
            {
                nameof(Int32) or nameof(Int64) or nameof(Decimal) or nameof(Byte) or nameof(SByte) or
                nameof(Double) or nameof(Single) or nameof(Int16) or nameof(UInt32) or nameof(UInt64) or
                nameof(UInt16) => value.ToString()!,

                nameof(Boolean) => databaseSouceType switch
                {
                    DatabaseSouceType.PostgreSQL => (bool)value ? "true" : "false",
                    DatabaseSouceType.SqlServer => (bool)value ? "1" : "0",
                    _ => value.ToString()!
                },

                nameof(String) => $"'{EscapeSqlQuote(value.ToString()!)}'",

                nameof(DateTime) => $"'{FormatDateTime((DateTime)value)}'",
                nameof(DateTimeOffset) => $"'{FormatDateTime(((DateTimeOffset)value).UtcDateTime)}'",

                _ when realType.IsEnum => ((int)value).ToString(),

                _ => $"'{EscapeSqlQuote(value.ToJson()!)}'"
            };
        }

        private static string EscapeSqlQuote(string input)
            => input.Replace("'", "''");

        private static string FormatDateTime(DateTime dt)
        {
            if (dt.TimeOfDay == TimeSpan.Zero)
                return dt.FormatToString(DateFormat.Date);
            return dt.FormatToString();
        }
    }
}