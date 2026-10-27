using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using Dapper;

using Viv.Contracts.Interface;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Dialect
{
    /// <summary>
    /// SQL 方言：命名、引用符、模板生成、字面量、CRUD 语句。
    ///
        /// 方言是**连接的属性**，不是调用方的参数：持有连接的那一方（<c>MomoDatabase</c>）在构造时
    /// 按 <see cref="DatabaseSourceType"/> 选定实现，其余代码只写 `dialect.Xxx(...)`。
    /// </summary>
    public interface ISqlDialect
    {
        /// <summary>本方言对应的数据库类型</summary>
        DatabaseSourceType Source { get; }

        #region 命名与引用符

        /// <summary>CLR 名 → 物理名：SQL Server 保持 PascalCase，PostgreSQL 转 snake_case</summary>
        string ToPhysical(string clrName);

        /// <summary>物理名加引用符：<c>[Name]</c> / <c>"name"</c></summary>
        string Quote(string physicalName);

        /// <summary>CLR 名直接加引用符（内部先 ToPhysical）</summary>
        string QuoteClr(string clrName);

        /// <summary>命名转换本身与方言无关，保留在接口里是为了让调用方只有一个入口</summary>
        string ToSnakeCase(string name);

        /// <summary>实体类型 → 带引号的物理表名（优先 [Table]，带缓存）</summary>
        string GetTableName<T>();

        /// <summary>字段名 → 带引号的物理列名</summary>
        string QuoteIdentifier(string field);

        #endregion

        #region 模板

        string GetInsertSqlTemplate(string tableName, Type type);

        string GetFindSqlTemplate(string tableName);

        /// <summary>按主键查询，另按逐条生效的读过滤器追加 WHERE</summary>
        string GetFindSqlTemplate(
            string tableName,
            long tenantId,
            IReadOnlyList<IMomoDataFilter> filters,
            IDictionary<string, object> parameters);

        /// <summary>分页语句（不同方言 TOP / LIMIT-OFFSET 写法不同）</summary>
        (string PageSql, string CountSql) GetPageSqlTemplate(string sql, int pageIndex, int pageSize);

        #endregion

        #region 条件删除

        (string Sql, Dictionary<string, object> Parameter) GetDeleteSql<T>(
            string tableName,
            Expression<Func<T, bool>> expression,
            long tenantId = 0);

        (string Sql, Dictionary<string, object> Parameter) GetSoftDeleteSql<T>(
            string tableName,
            Expression<Func<T, bool>> expression,
            long tenantId = 0)
            where T : IEntity, ISoftDeleted;

        #endregion

        #region 字面量

        /// <summary>值 → 方言下的 SQL 字面量（bool / 日期 / Guid 的写法两边不同）</summary>
        string ToDatabaseValue(object? value);

        #endregion

        #region CRUD 语句

        (string Sql, DynamicParameters Parameters) CreateInsertSql(string tableName, object entity, string ignoreKeys = "");

        (string Sql, DynamicParameters Parameters) CreateUpdateSql(string tableName, object entity, string whereKeys, string ignoreKeys = "");

        (string Sql, DynamicParameters Parameters) CreateDeleteSql(string tableName, object entity);

        string CreateInsertSqlRaw(string tableName, object entity, string ignoreKeys = "");

        string CreateUpdateSqlRaw(string tableName, object entity, string whereKeys, string ignoreKeys = "");

        string CreateDeleteSqlRaw(string tableName, object entity);

        #endregion
    }
}
