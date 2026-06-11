using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.SqlServer
{
    /// <summary>
    /// MSSQL SQL 分析器。
    /// 负责把 SQL 拆成 BaseSql、OrderBySql，并提取各种标记。
    /// </summary>
    public sealed class MssqlSqlAnalyzer
    {
        private readonly TSqlQueryParser _parser;

        public MssqlSqlAnalyzer()
            : this(new TSqlQueryParser())
        {
        }

        public MssqlSqlAnalyzer(TSqlQueryParser parser)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        public TSqlAnalysis Analyze(string sql)
        {
            var selectStatement = _parser.ParseSelect(sql);

            var normalizedSql = MssqlSqlScriptGenerator.Generate(selectStatement);

            var orderByClause = GetTopLevelOrderBy(selectStatement);
            var offsetClause = GetTopLevelOffset(selectStatement);
            var querySpecification = GetQuerySpecification(selectStatement);

            var baseSql = BuildBaseSql(sql);
            var orderBySql = orderByClause == null ? string.Empty : MssqlSqlScriptGenerator.Generate(orderByClause);

            return new TSqlAnalysis
            {
                OriginalSql = sql.Trim(),
                NormalizedSql = normalizedSql,
                BaseSql = baseSql,
                OrderBySql = orderBySql,

                HasOrderBy = orderByClause != null,
                HasOffsetFetch = offsetClause != null,

                HasTop = querySpecification?.TopRowFilter != null,
                HasDistinct = querySpecification?.UniqueRowFilter == UniqueRowFilter.Distinct,
                HasGroupBy = querySpecification?.GroupByClause != null,

                IsSetQuery = IsSetQuery(selectStatement.QueryExpression),
                HasCte = selectStatement.WithCtesAndXmlNamespaces != null
            };
        }

        private string BuildBaseSql(string sql)
        {
            var selectStatement = _parser.ParseSelect(sql);

            if (selectStatement.QueryExpression != null)
            {
                selectStatement.QueryExpression.OrderByClause = null;
                selectStatement.QueryExpression.OffsetClause = null;
            }

            return MssqlSqlScriptGenerator.Generate(selectStatement);
        }

        private static OrderByClause? GetTopLevelOrderBy(SelectStatement selectStatement)
        {
            return selectStatement.QueryExpression?.OrderByClause;
        }

        private static OffsetClause? GetTopLevelOffset(SelectStatement selectStatement)
        {
            return selectStatement.QueryExpression?.OffsetClause;
        }

        private static QuerySpecification? GetQuerySpecification(SelectStatement selectStatement)
        {
            return selectStatement.QueryExpression as QuerySpecification;
        }

        private static bool IsSetQuery(QueryExpression? queryExpression)
        {
            return queryExpression is BinaryQueryExpression;
        }

    }
}
