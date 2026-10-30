using Viv.Apex.Core.Entity.Dto.Menu;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>给指定应用新增或更新菜单。Id 为 0 是新增。</summary>
    public class SaveAppMenuRequest : SaveMenuRequest
    {
        public long ClientAppId { get; set; }
    }
}
