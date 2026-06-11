using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.SqlServer
{
    /// <summary>
    /// MSSQL SQL 分析结果。
    /// 只保存结果，不负责解析，不负责生成分页 SQL。
    /// </summary>
    public sealed class TSqlAnalysis
    {
        /// <summary>
        /// 原始 SQL。
        /// </summary>
        public string OriginalSql { get; init; } = string.Empty;

        /// <summary>
        /// 格式化后的完整 SQL。
        /// </summary>
        public string NormalizedSql { get; init; } = string.Empty;

        /// <summary>
        /// 去掉最外层 ORDER BY 和 OFFSET/FETCH 后的 SQL。
        /// 通常用于包 Count 查询。
        /// </summary>
        public string BaseSql { get; init; } = string.Empty;

        /// <summary>
        /// 最外层 ORDER BY 片段。
        /// 例如：ORDER BY a.CreateTime DESC, a.Id ASC
        /// </summary>
        public string OrderBySql { get; init; } = string.Empty;

        /// <summary>
        /// 是否有最外层 ORDER BY。
        /// </summary>
        public bool HasOrderBy { get; init; }

        /// <summary>
        /// 是否已经有 OFFSET/FETCH。
        /// </summary>
        public bool HasOffsetFetch { get; init; }

        /// <summary>
        /// 是否有 TOP。
        /// </summary>
        public bool HasTop { get; init; }

        /// <summary>
        /// 是否有 DISTINCT。
        /// </summary>
        public bool HasDistinct { get; init; }

        /// <summary>
        /// 是否有 GROUP BY。
        /// </summary>
        public bool HasGroupBy { get; init; }

        /// <summary>
        /// 是否是 UNION / UNION ALL / EXCEPT / INTERSECT 这类组合查询。
        /// </summary>
        public bool IsSetQuery { get; init; }

        /// <summary>
        /// 是否有 CTE。
        /// </summary>
        public bool HasCte { get; init; }
    }
}
