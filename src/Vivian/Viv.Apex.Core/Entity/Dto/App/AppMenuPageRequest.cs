using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>某个应用下的菜单分页。关键字匹配菜单名称。</summary>
    public class AppMenuPageRequest : VivApiPagedRequest
    {
        public AppMenuPageRequest()
        {
            PageIndex = 1;
            PageSize = 20;
        }

        /// <summary>菜单所属的客户端应用 Id。</summary>
        public long ClientAppId { get; set; }

        /// <summary>关键字，匹配菜单名称，可空。</summary>
        [StringLength(100)]
        [DisplayName("关键字")]
        public string? Keyword { get; set; }

        /// <summary>
        /// 分页查询。父级名称与下级数量在 SQL 里一起取，省掉逐行回查；
        /// 软删除由 SQL 自己挡（原 SQL 不走 EF 全局过滤器）。
        /// 条件用 StringBuilder 逐条追加，后面加筛选直接再 Append 一段。
        /// </summary>
        public override (string sql, object parameters) GetSqlQuery()
        {
            var keyword = Keyword?.Trim();

            var sql = new StringBuilder("""
                SELECT m.Id,
                       m.ParentId,
                       m.Title,
                       m.Type,
                       m.Icon,
                       m.Sort,
                       m.IsVisible,
                       m.RouteConfig,
                       m.Description,
                       m.Status,
                       COALESCE(p.Title, '') AS ParentTitle,
                       (SELECT COUNT(*) FROM AtMenu c WHERE c.ParentId = m.Id AND c.IsDeleted = 0) AS ChildCount
                FROM AtMenu m
                LEFT JOIN AtMenu p ON p.Id = m.ParentId AND p.IsDeleted = 0
                WHERE m.IsDeleted = 0 AND m.ClientAppId = @ClientAppId
                """);

            if (!string.IsNullOrEmpty(keyword))
                sql.Append(" AND m.Title LIKE @Keyword");

            sql.AppendLine().Append("ORDER BY m.ParentId, m.Sort, m.Id");

            return (sql.ToString(), new { ClientAppId, Keyword = $"%{keyword}%" });
        }
    }
}
