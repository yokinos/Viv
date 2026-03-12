using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Contracts.Enums;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Vva;

namespace Viv.Momo
{
    /// <summary>
    /// 通用数据操作工具类
    /// 包含：表名获取、查询条件拼接、SQL模板生成（插入/查询/分页）等核心方法
    /// </summary>
    public static class DataSqlBuilder
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
        public static string GetTableName<T>()
        {
            var entityType = typeof(T);
            if (!_tableNameCache.TryGetValue(entityType, out var tableName))
            {
                var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
                tableName = tableAttr?.Name ?? entityType.Name;
                _tableNameCache[entityType] = tableName;
            }

            return tableName;
        }
        public static Expression<Func<T, bool>> AutoSpliceCommonCondition<T>(Expression<Func<T, bool>> predicate, long tenantId, long vivAppId)
        {
            Expression<Func<T, bool>> finalPredicate = predicate;
            if (typeof(T).IsAssignableFrom(typeof(EntityBase)))
            {
                Expression<Func<T, bool>> softDeleteExpr = x => (x as EntityBase).IsDeleted == VivBool.False;
                Expression<Func<T, bool>> tenantExpr = x => (x as EntityBase).TenantId == tenantId;
                Expression<Func<T, bool>> appIdExpr = x => (x as EntityBase).AppId == vivAppId;

                finalPredicate = CombineExpressions(finalPredicate, softDeleteExpr);
                finalPredicate = CombineExpressions(finalPredicate, tenantExpr);
                finalPredicate = CombineExpressions(finalPredicate, appIdExpr);
            }

            return finalPredicate;
        }

        private static Expression<Func<T, bool>> CombineExpressions<T>(Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var body1 = new ParameterReplacer(param).Visit(expr1.Body);
            var body2 = new ParameterReplacer(param).Visit(expr2.Body);
            var combined = Expression.AndAlso(body1, body2);

            return Expression.Lambda<Func<T, bool>>(combined, param);
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
            if (databaseSouceType == DatabaseSouceType.PostgreSQL)
            {
                var nameListLower = propertyNameList.Select(x => x.ToLowerInvariant()).ToList();
                return $"INSERT INTO {tableName.ToLowerInvariant()}({string.Join(",", nameListLower)}) VALUES({string.Join(",", nameListLower.Select(x => $"@{x}"))})";
            }

            var sql = $"INSERT INTO {tableName}({string.Join(",", propertyNameList)}) VALUES({string.Join(",", propertyNameList.Select(x => $"@{x}"))})";
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
            if (databaseSouceType == DatabaseSouceType.PostgreSQL)
            {
                return $"SELECT * FROM {tableName.ToLowerInvariant()} WHERE id = @Id";
            }

            return $"SELECT * FROM [{tableName}] WHERE Id = @Id";
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
            var countSql = $"SELECT COUNT(*) FROM ({sql}) AS t";
            string pageSql;

            if (databaseSouceType == DatabaseSouceType.PostgreSQL)
            {
                int offset = (pageIndex - 1) * pageSize;
                pageSql = $"SELECT * FROM ({sql}) AS t LIMIT {pageSize} OFFSET {offset}";
            }
            else
            {
                int offset = (pageIndex - 1) * pageSize;
                pageSql = $"SELECT * FROM ({sql}) AS t OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
            }

            return (pageSql, countSql);
        }
    }
}