using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>删除指定应用下的一个子页面。</summary>
    public class DeleteAppMenuSubPageRequest : VivApiRequest
    {
        /// <summary>子页面所属的客户端应用 Id</summary>
        public long ClientAppId { get; set; }

        /// <summary>要删除的子页面 Id</summary>
        public long Id { get; set; }
    }
}
