using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.SqlServer
{

    /// <summary>
    /// MSSQL 分页 SQL 构建器。
    /// 只负责基于分析结果生成 CountSql 和 PageSql。
    /// </summary>
    public sealed class MssqlPagingSqlBuilder
    {
        public static string BuildCountSql(TSqlAnalysis analysis, string alias = "T")
        {
            if (analysis == null)
                throw new ArgumentNullException(nameof(analysis));

            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("别名不能为空。", nameof(alias));

            return $"SELECT COUNT(1)FROM ({analysis.BaseSql}) AS {alias}".Trim();
        }

        public static string BuildPageSql(TSqlAnalysis analysis, int pageIndex, int pageSize)
        {
            if (analysis == null)
                throw new ArgumentNullException(nameof(analysis));

            if (pageIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "pageIndex 必须大于等于 1。");

            if (pageSize < 1)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize 必须大于等于 1。");

            if (!analysis.HasOrderBy)
                throw new InvalidOperationException("分页 SQL 必须包含最外层 ORDER BY。");

            if (analysis.HasOffsetFetch)
                throw new InvalidOperationException("原 SQL 已经包含 OFFSET/FETCH，不能重复分页。");

            var offset = (pageIndex - 1) * pageSize;

            return $"{analysis.BaseSql} {analysis.OrderBySql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY".Trim();
        }
    }

}
