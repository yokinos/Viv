using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Viv.Momo.Dialect;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Converter
{
    /// <summary>
    /// 表达式树 → 通用参数化 SQL。
    ///
    /// 实例类型：方言在构造时定死，调用方不必再每次传 <c>DatabaseSourceType</c>
    /// （原来那个参数既容易传错，又让"谁决定方言"变得模糊）。
    /// </summary>
    public class ExpressionToSqlConverter
    {
        private readonly ISqlDialect _dialect;

        public ExpressionToSqlConverter(ISqlDialect dialect)
        {
            _dialect = dialect;
        }

        /// <summary>
        /// 将表达式解析为 通用参数化 SQL
        /// 自动适配：MSSQL / PostgreSQL
        /// </summary>
        public (string Sql, Dictionary<string, object> Parameter) Convert<T>(Expression<Func<T, bool>> expression)
        {
            if (expression == null)
                return (string.Empty, []);

            // visitor 有状态（Sql / Parameters 是实例字段），每次调用必须新建
            var visitor = new SqlExpressionVisitor(_dialect);
            visitor.Visit(expression);

            return (visitor.Sql.ToString(), visitor.Parameters);
        }
    }
}
