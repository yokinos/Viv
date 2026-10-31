using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>后台菜单动作（权限点）。动作不能独立存在，永远挂在某个页面菜单下。</summary>
    public class MenuAdminAction
    {
        /// <summary>动作 Id（AtMenuAction.Id）</summary>
        public long Id { get; set; }

        /// <summary>所属页面菜单 Id</summary>
        public long MenuId { get; set; }

        /// <summary>动作名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>备注（仅后台使用）</summary>
        public string? Remark { get; set; }

        /// <summary>启用或禁用</summary>
        public EmStatus Status { get; set; }

        /// <summary>从实体映射。</summary>
        public static MenuAdminAction From(AtMenuAction action) => new()
        {
            Id = action.Id,
            MenuId = action.MenuId,
            Name = action.Name ?? string.Empty,
            Sort = action.Sort,
            Remark = action.Remark,
            Status = action.Status
        };
    }
}
