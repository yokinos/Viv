using Viv.Apex.Core.Entity.Vo.Menu;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Module.Menu
{
    /// <summary>
    /// 后台菜单树。运行时路由树只含启用节点，这里把停用的也留下，方便改回来。
    /// 父节点已经不在的行挂到根上，避免改不着。
    /// </summary>
    public static class MenuAdminTree
    {
        public static List<MenuAdminNode> Build(IReadOnlyList<AtMenu> menus, IReadOnlyList<AtMenuSubPage>? subPages = null,
            IReadOnlyList<AtMenuAction>? actions = null)
        {
            var ids = menus.Select(x => x.Id).ToHashSet();
            var byParent = menus.ToLookup(menu =>
                menu.ParentId > 0 && ids.Contains(menu.ParentId) ? menu.ParentId : 0L);

            var tree = BuildLevel(byParent, 0);
            if (subPages is { Count: > 0 })
                Attach(tree, subPages.ToLookup(page => page.MenuId));
            if (actions is { Count: > 0 })
                AttachActions(tree, actions.ToLookup(action => action.MenuId));
            return tree;
        }

        /// <summary>子页面挂在所属菜单后面，排在子菜单之后。</summary>
        private static void Attach(List<MenuAdminNode> nodes, ILookup<long, AtMenuSubPage> byMenu)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count > 0)
                    Attach(node.Children, byMenu);

                foreach (var page in byMenu[node.Id].OrderBy(x => x.Sort).ThenBy(x => x.Id))
                {
                    node.Children.Add(new MenuAdminNode
                    {
                        Id = page.Id,
                        ParentId = page.MenuId,
                        Title = page.Title ?? string.Empty,
                        Type = EmMenuType.Page,
                        Sort = page.Sort,
                        IsVisible = page.IsVisible,
                        RouteConfig = page.RouteConfig,
                        Description = page.Remark,
                        Status = page.Status,
                        IsSubPage = true
                    });
                }
            }
        }

        /// <summary>动作挂在所属页面菜单上，单独一个列表（不混进子页面）。</summary>
        private static void AttachActions(List<MenuAdminNode> nodes, ILookup<long, AtMenuAction> byMenu)
        {
            foreach (var node in nodes)
            {
                // 子页面不是菜单节点，动作只挂在 AtMenu 上
                if (!node.IsSubPage)
                {
                    foreach (var action in byMenu[node.Id].OrderBy(x => x.Sort).ThenBy(x => x.Id))
                    {
                        node.Actions.Add(new MenuAdminAction
                        {
                            Id = action.Id,
                            MenuId = action.MenuId,
                            Name = action.Name ?? string.Empty,
                            Sort = action.Sort,
                            Remark = action.Remark,
                            Status = action.Status
                        });
                    }
                }

                if (node.Children.Count > 0)
                    AttachActions(node.Children, byMenu);
            }
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
