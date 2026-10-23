using System.Text.Json;
using Viv.Apex.Core.Module.Menu.Vue;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Module.Menu
{
    /// <summary>
    /// Vue Router 适配器。AtMenu.RouteConfig 存的就是一个 <see cref="VueRouteItem"/>（不含 children），
    /// 反序列化后把列上的展示信息盖上去：title/icon/orderNo/hidden 以列为准，children 由树结构生成，
    /// JSON 里写了这两个字段也不生效。组树也在这里做 —— 别的技术栈的结构可能不是树。
    /// </summary>
    [VivDependency(EmClientStack.Vue)]
    public class VueMenuRouteAdapter : IMenuRouteAdapter, IDependency
    {
        /// <summary>目录节点的组件标识，Vue 端约定用它渲染布局外壳</summary>
        public const string LayoutComponent = "LAYOUT";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public EmClientStack Stack => EmClientStack.Vue;

        public object BuildTree(IReadOnlyList<AtMenu> menus, IReadOnlyList<AtMenuSubPage> subPages)
        {
            var byParent = menus.ToLookup(x => x.ParentId);
            var byMenu = subPages.ToLookup(x => x.MenuId);

            return Build(byParent[0], null, byParent, byMenu);
        }

        private static List<VueRouteItem> Build(
            IEnumerable<AtMenu> nodes,
            VueRouteItem? parent,
            ILookup<long, AtMenu> byParent,
            ILookup<long, AtMenuSubPage> byMenu)
        {
            var result = new List<VueRouteItem>();

            foreach (var menu in nodes.OrderBy(x => x.Sort))
            {
                var route = BuildItem(menu, parent);

                route.Children.AddRange(Build(byParent[menu.Id], route, byParent, byMenu));

                // 子页面是页面内部的视图，挂在同一个 children 列表后面
                route.Children.AddRange(byMenu[menu.Id]
                    .OrderBy(x => x.Sort)
                    .Select(page => BuildSubPage(page, route)));

                result.Add(route);
            }

            return result;
        }

        private static VueRouteItem BuildItem(AtMenu menu, VueRouteItem? parent)
        {
            var item = Parse(menu.RouteConfig) ?? new VueRouteItem();

            item.Path = Combine(parent?.Path, item.Path);
            item.Component = menu.Type == EmMenuType.Directory ? LayoutComponent : item.Component;
            item.Meta ??= new VueRouteMeta();
            item.Meta.Title = menu.Title;
            item.Meta.Icon = menu.Icon;
            item.Meta.OrderNo = menu.Sort;
            // 隐藏的菜单项照样注册路由，只是侧边栏不显示
            item.Meta.Hidden = !menu.IsVisible;
            item.Meta.KeepAlive ??= true;
            item.Children = [];

            return item;
        }

        private static VueRouteItem BuildSubPage(AtMenuSubPage page, VueRouteItem parent)
        {
            var item = Parse(page.RouteConfig) ?? new VueRouteItem();

            item.Path = Combine(parent.Path, item.Path);
            item.Meta ??= new VueRouteMeta();
            item.Meta.Title = page.Title;
            item.Meta.OrderNo = page.Sort;
            item.Meta.Hidden = !page.IsVisible;
            item.Meta.KeepAlive ??= true;
            item.Children = [];

            return item;
        }

        /// <summary>
        /// 子路径拼接。Vue Router 的规则：以 / 开头就是根路径（不拼父级），
        /// 相对路径才拼到父路径后面；http(s) 原样当外链
        /// </summary>
        private static string? Combine(string? parentPath, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return parentPath ?? "/";

            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;

            // 绝对路径：Vue 里就是根路径，父级不参与
            if (path.StartsWith('/')) return path;

            return parentPath is null ? "/" + path : parentPath.TrimEnd('/') + "/" + path;
        }

        private static VueRouteItem? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<VueRouteItem>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // 配错 JSON 不该让整棵菜单挂掉：按"没配"处理，其余节点照常输出
                return null;
            }
        }
    }
}
