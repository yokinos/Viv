using System.Text.Json;
using System.Text.Json.Serialization;
using Viv.Apex.Core.Module.Menu.Vue;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Module.Menu
{
    /// <summary>
    /// Vue Router 适配器。RouteConfig 的约定见 <see cref="VueRouteConfig"/>，
    /// 缺项一律给 Vue 端的默认值，前端不用再兜底。
    /// 组树也在这里做：别的技术栈的结构可能根本不是树，不该塞进公共接口。
    /// </summary>
    [VivDependency(EmClientStack.Vue)]
    public class VueMenuRouteAdapter : IMenuRouteAdapter, IDependency
    {
        /// <summary>目录节点的组件标识，Vue 端约定用它渲染布局外壳</summary>
        public const string LayoutComponent = "LAYOUT";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
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
            var config = Parse(menu.RouteConfig);

            return new VueRouteItem
            {
                Path = Combine(parent?.Path, config?.Path),
                Name = config?.Name,
                Component = menu.Type == EmMenuType.Directory ? LayoutComponent : config?.Component,
                Redirect = config?.Redirect,
                Meta = new VueRouteMeta
                {
                    Title = menu.Title,
                    Icon = menu.Icon,
                    OrderNo = menu.Sort,
                    // 纯路由不进菜单；显式隐藏的也按隐藏走
                    Hidden = menu.Type == EmMenuType.Route || !menu.IsVisible,
                    KeepAlive = config?.Meta?.KeepAlive ?? true,
                    Expanded = config?.Meta?.Expanded,
                    Single = config?.Meta?.Single,
                    HiddenBreadcrumb = config?.Meta?.HiddenBreadcrumb,
                    FrameSrc = config?.Meta?.FrameSrc,
                    FrameBlank = config?.Meta?.FrameBlank
                }
            };
        }

        private static VueRouteItem BuildSubPage(AtMenuSubPage page, VueRouteItem parent)
        {
            var config = Parse(page.RouteConfig);

            return new VueRouteItem
            {
                Path = Combine(parent.Path, config?.Path),
                Name = config?.Name,
                Component = config?.Component,
                Redirect = config?.Redirect,
                Meta = new VueRouteMeta
                {
                    Title = page.Title,
                    OrderNo = page.Sort,
                    Hidden = !page.IsVisible,
                    KeepAlive = config?.Meta?.KeepAlive ?? true,
                    Expanded = config?.Meta?.Expanded,
                    Single = config?.Meta?.Single,
                    HiddenBreadcrumb = config?.Meta?.HiddenBreadcrumb,
                    FrameSrc = config?.Meta?.FrameSrc,
                    FrameBlank = config?.Meta?.FrameBlank
                }
            };
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

        private static VueRouteConfig? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<VueRouteConfig>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // 配错 JSON 不该让整棵菜单挂掉：按"没配"处理，其余节点照常输出
                return null;
            }
        }
    }

    /// <summary>Vue RouteConfig 的约定。未列出的键会被忽略，不会报错</summary>
    public class VueRouteConfig
    {
        public string? Path { get; set; }

        public string? Name { get; set; }

        public string? Component { get; set; }

        public string? Redirect { get; set; }

        public VueRouteMetaConfig? Meta { get; set; }
    }

    public class VueRouteMetaConfig
    {
        public bool? KeepAlive { get; set; }

        public bool? Expanded { get; set; }

        public bool? Single { get; set; }

        public bool? HiddenBreadcrumb { get; set; }

        public string? FrameSrc { get; set; }

        public bool? FrameBlank { get; set; }
    }
}
