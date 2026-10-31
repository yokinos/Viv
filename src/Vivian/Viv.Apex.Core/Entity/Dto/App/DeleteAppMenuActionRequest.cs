using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>删除指定应用下的一个菜单动作（权限点）。</summary>
    public class DeleteAppMenuActionRequest : VivApiRequest
    {
        /// <summary>动作所属的客户端应用 Id</summary>
        public long ClientAppId { get; set; }

        /// <summary>要删除的动作 Id</summary>
        public long Id { get; set; }
    }
}
