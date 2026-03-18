using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Viv.Momo.Enums;

namespace Viv.Momo.Converter
{
    public class ExpressionToSqlConverter
    {
        /// <summary>
        /// 将表达式解析为 通用参数化 SQL
        /// 自动适配：MSSQL / PostgreSQL / MySQL
        /// </summary>
        public static (string sql, Dictionary<string, object> parameter) Convert<T>(Expression<Func<T, bool>> expression)
        {
            if (expression == null)
                return (string.Empty, []);

            var visitor = new SqlExpressionVisitor();
            visitor.Visit(expression);

            return (visitor.Sql.ToString(), visitor.Parameters);
        }

        public static (string sql, Dictionary<string, object> parameter) GetDeleteSql<T>(string tableName, Expression<Func<T, bool>> expression, DatabaseSouceType databaseSouceType)
        {
            var (where, parameters) = Convert(expression);

            var sql = $"DELETE FROM {tableName} WHERE {where}";
            return (sql, parameters);
        }
    }
}
