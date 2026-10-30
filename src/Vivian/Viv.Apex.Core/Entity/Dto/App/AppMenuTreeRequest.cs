using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>读取指定应用的菜单树，不看当前登录应用。</summary>
    public class AppMenuTreeRequest : VivApiRequest
    {
        public long ClientAppId { get; set; }
    }
}
