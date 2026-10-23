using System.Reflection;
using Viv.Apex.Core.Module.Menu;
using Viv.Apex.Core.Module.Menu.Vue;
using Viv.Contracts.Attributes;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    /// <summary>Vue 适配器：路径拼接、目录组件、隐藏规则、子页面挂载、坏 JSON 的兜底</summary>
    public class VueMenuRouteAdapterTests
    {
        private readonly VueMenuRouteAdapter _adapter = new();

        private static AtMenu Menu(
            long id,
            EmMenuType type,
            string? routeConfig,
            long parentId = 0,
            int sort = 0,
            bool visible = true) => new()
        {
            Id = id,
            ParentId = parentId,
            Type = type,
            Title = "菜单" + id,
            Icon = "icon",
            Sort = sort,
            IsVisible = visible,
            RouteConfig = routeConfig
        };

        private List<VueRouteItem> Build(params AtMenu[] menus)
            => (List<VueRouteItem>)_adapter.BuildTree(menus, []);

        [Fact]
        public void BuildTree_目录固定用LAYOUT组件()
        {
            var route = Assert.Single(Build(Menu(1, EmMenuType.Directory, """{"path":"/system","name":"system"}""")));

            Assert.Equal(VueMenuRouteAdapter.LayoutComponent, route.Component);
            Assert.Equal("/system", route.Path);
            Assert.Equal("system", route.Name);
        }

        [Fact]
        public void BuildTree_子菜单按父级分组并拼路径()
        {
            var tree = Build(
                Menu(1, EmMenuType.Directory, """{"path":"/system"}""", sort: 1),
                Menu(2, EmMenuType.Page, """{"path":"user","name":"system-user"}""", parentId: 1, sort: 2));

            var root = Assert.Single(tree);
            var child = Assert.Single(root.Children);
            Assert.Equal("/system", root.Path);
            Assert.Equal("/system/user", child.Path);
            Assert.Equal("system-user", child.Name);
        }

        [Fact]
        public void BuildTree_绝对路径不拼父级_外链原样保留()
        {
            var tree = Build(
                Menu(1, EmMenuType.Directory, """{"path":"/system"}"""),
                Menu(2, EmMenuType.Page, """{"path":"/other"}""", parentId: 1),
                Menu(3, EmMenuType.Page, """{"path":"https://example.com/x"}""", parentId: 1));

            var children = tree[0].Children;
            Assert.Equal("/other", children[0].Path);
            Assert.Equal("https://example.com/x", children[1].Path);
        }

        [Fact]
        public void BuildTree_隐藏的菜单项仍然出路由_只是标隐藏()
        {
            var tree = Build(
                Menu(1, EmMenuType.Page, """{"path":"detail/:id"}""", visible: false),
                Menu(2, EmMenuType.Page, null));

            Assert.True(tree[0].Meta!.Hidden);
            Assert.Equal("/detail/:id", tree[0].Path);
            Assert.False(tree[1].Meta!.Hidden);
        }

        [Fact]
        public void BuildTree_同级按Sort排序()
        {
            var tree = Build(
                Menu(1, EmMenuType.Page, """{"path":"b"}""", sort: 20),
                Menu(2, EmMenuType.Page, """{"path":"a"}""", sort: 10));

            Assert.Equal(["/a", "/b"], tree.Select(x => x.Path));
        }

        [Fact]
        public void BuildTree_没配路由配置时也能出节点并给默认值()
        {
            var route = Assert.Single(Build(Menu(1, EmMenuType.Page, null)));

            Assert.Equal("菜单1", route.Meta!.Title);
            Assert.True(route.Meta.KeepAlive);
            Assert.Equal("/", route.Path);
        }

        [Fact]
        public void BuildTree_路由配置是坏JSON时按没配处理()
        {
            var route = Assert.Single(Build(Menu(1, EmMenuType.Page, "{不是 json")));

            Assert.Null(route.Name);
            Assert.Equal("/", route.Path);
        }

        [Fact]
        public void BuildTree_子页面挂在父节点children末尾并继承父路径()
        {
            var menus = new[] { Menu(1, EmMenuType.Page, """{"path":"/system/user"}""") };
            var pages = new[]
            {
                new AtMenuSubPage
                {
                    Id = 11,
                    MenuId = 1,
                    Title = "基础信息",
                    Sort = 2,
                    IsVisible = true,
                    RouteConfig = """{"path":"base","name":"system-user-base","component":"/system/user/base"}"""
                }
            };

            var tree = (List<VueRouteItem>)_adapter.BuildTree(menus, pages);

            var route = Assert.Single(tree);
            var sub = Assert.Single(route.Children);
            Assert.Equal("/system/user/base", sub.Path);
            Assert.Equal("system-user-base", sub.Name);
            Assert.Equal("/system/user/base", sub.Component);
            Assert.Equal(2, sub.Meta!.OrderNo);
            Assert.False(sub.Meta.Hidden);
        }

        [Fact]
        public void Vue适配器注册在Vue这个key上()
        {
            // 适配器靠 [VivDependency(EmClientStack.Vue)] 做 keyed 注册；
            // 这个 key 必须和 AtClientApp.ClientStack 的取值一致，否则运行时 TryGetValue 会落空
            var tag = typeof(VueMenuRouteAdapter)
                .GetCustomAttribute<VivDependencyAttribute>()?.Tag;

            Assert.Equal(EmClientStack.Vue, tag);
            Assert.Equal(EmClientStack.Vue, new AtClientApp().ClientStack);
            Assert.Equal(EmClientStack.Vue, _adapter.Stack);
        }
    }
}
