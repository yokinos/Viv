using Dapper;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>应用分页。关键字匹配名称或 AppKey。</summary>
    public class AppPageRequest : VivApiPagedRequest
    {
        /// <summary>关键字，匹配名称或 AppKey，可空。</summary>
        [StringLength(100)]
        [DisplayName("关键字")]
        public string? Keyword { get; set; }

        /// <summary>
        /// 分页查询。软删除在 SQL 里挡（原 SQL 不走 EF 全局过滤器）。
        /// 条件用 StringBuilder 逐条追加，后面加筛选直接再 Append 一段。
        /// </summary>
        public override (string sql, object parameters) GetSqlQuery()
        {
            var parameters = new DynamicParameters();

            var sql = new StringBuilder("SELECT * FROM AtClientApp WHERE IsDeleted = 0");
            if (!Keyword.IsNullOrEmpty())
            {
                sql.Append(" AND (Name LIKE @Keyword OR AppKey LIKE @Keyword)");
                parameters.Add("@Keyword", $"%{Keyword.Trim()}%");
            }

            sql.Append(" ORDER BY Id ASC");

            return (sql.ToString(), parameters);
        }
    }
}
