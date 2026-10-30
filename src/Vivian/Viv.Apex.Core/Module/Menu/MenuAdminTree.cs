using Viv.Apex.Core.Entity.Vo.Menu;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.Module.Menu
{
    /// <summary>
    /// 后台菜单树。运行时路由树只含启用节点，这里把停用的也留下，方便改回来。
    /// 父节点已经不在的行挂到根上，避免改不着。
    /// </summary>
    public static class MenuAdminTree
    {
        public static List<MenuAdminNode> Build(IReadOnlyList<AtMenu> menus)
        {
            var ids = menus.Select(x => x.Id).ToHashSet();
            var byParent = menus.ToLookup(menu =>
                menu.ParentId > 0 && ids.Contains(menu.ParentId) ? menu.ParentId : 0L);

            return BuildLevel(byParent, 0);
        }

        private static List<MenuAdminNode> BuildLevel(ILookup<long, AtMenu> byParent, long parentId)
        {
            return byParent[parentId]
                .OrderBy(x => x.Sort)
                .ThenBy(x => x.Id)
                .Select(menu => new MenuAdminNode
                {
                    Id = menu.Id,
                    ParentId = menu.ParentId,
                    Title = menu.Title ?? string.Empty,
                    Type = menu.Type,
                    Icon = menu.Icon,
                    Sort = menu.Sort,
                    IsVisible = menu.IsVisible,
                    RouteConfig = menu.RouteConfig,
                    Description = menu.Description,
                    Status = menu.Status,
                    Children = BuildLevel(byParent, menu.Id)
                })
                .ToList();
        }
    }
}
