using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Delusion.Extension;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Entity.Vue;

namespace Viv.Entity.Converters
{
    /// <summary>
    /// Vue Router 路由转换器
    /// 将 AtMenu 和 AtMenuSubPage 转换为前端 Vue Router 路由树
    /// </summary>
    public class VueRouterConverter
    {
        /// <summary>
        /// 转换为 Vue Router 路由树
        /// </summary>
        /// <param name="menus"></param>
        /// <param name="subPages"></param>
        public static List<RouteItem> ToVueRouter(List<AtMenu> menus, List<AtMenuSubPage>? subPages = null)
        {
            if (menus.IsNullOrEmpty())
                return [];

            var menuList = menus.OrderBy(m => m.Sort).ToList();
            var menuLookup = menuList.ToLookup(m => m.ParentId);
            var subPageLookup = subPages?
                .Where(s => s.Status == EmStatus.Enabled && !s.IsDeleted)
                .OrderBy(s => s.Sort)
                .ToLookup(s => s.MenuId) ?? Enumerable.Empty<AtMenuSubPage>().ToLookup(s => s.MenuId);

            var rootMenus = menuList.Where(m => m.ParentId == 0);
            return BuildTree(rootMenus, menuLookup, subPageLookup);
        }

        private static List<RouteItem> BuildTree(IEnumerable<AtMenu> menus, ILookup<long, AtMenu> menuLookup, ILookup<long, AtMenuSubPage> subPageLookup)
        {
            var result = new List<RouteItem>();

            foreach (var menu in menus.OrderBy(m => m.Sort))
            {
                var item = new RouteItem
                {
                    Path = menu.Path,
                    Name = menu.RouteName,
                    Component = menu.Type == EmMenuType.Directory ? "LAYOUT" : menu.Component,
                    Redirect = menu.Redirect,
                    Meta = new RouteMeta
                    {
                        Title = menu.Title,
                        Icon = menu.Icon,
                        OrderNo = menu.Sort,
                        Hidden = !menu.IsVisible,
                        KeepAlive = menu.IsKeepAlive,
                        Expanded = menu.IsExpanded,
                        Single = menu.IsSingle,
                        HiddenBreadcrumb = menu.IsHiddenBreadcrumb,
                        FrameSrc = menu.IsExternalLink ? menu.Path : null,
                        FrameBlank = menu.IsOpenNewTab
                    },
                    Children = []
                };

                // 递归子菜单
                var childMenus = menuLookup[menu.Id];
                if (childMenus.Any())
                {
                    item.Children.AddRange(BuildTree(childMenus, menuLookup, subPageLookup));
                }

                // 子页面
                var childPages = subPageLookup[menu.Id]
                    .OrderBy(s => s.Sort)
                    .Select(s => new RouteItem
                    {
                        Path = s.Path,
                        Name = s.RouteName,
                        Component = s.Component,
                        Meta = new RouteMeta
                        {
                            Title = s.Title,
                            OrderNo = s.Sort,
                            KeepAlive = s.IsKeepAlive,
                            Hidden = !s.IsVisible
                        }
                    });

                if (childPages.Any())
                {
                    item.Children.AddRange(childPages);
                }

                result.Add(item);
            }

            return result;
        }
    }
}