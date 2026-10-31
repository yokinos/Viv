using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>删除指定应用下的一个菜单。有子节点时不允许删。</summary>
    public class DeleteAppMenuRequest : VivApiRequest
    {
        /// <summary>菜单所属的客户端应用 Id。</summary>
        public long ClientAppId { get; set; }

        /// <summary>要删除的菜单 Id。</summary>
        public long Id { get; set; }
    }
}
