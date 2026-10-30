using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>应用分页。关键字匹配名称或 AppKey。</summary>
    public class AppPageRequest : VivApiRequest
    {
        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        [StringLength(100)]
        public string? Keyword { get; set; }
    }
}
