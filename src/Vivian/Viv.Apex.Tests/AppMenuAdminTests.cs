using System.Reflection;
using Autofac.Features.Indexed;
using Viv.Apex.Core.Entity.CacheBucket;
using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.Entity.Vo.Menu;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.Module.Menu;
using Viv.Apex.Core.Service;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Engine;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    public class AppMenuAdminTests
    {
        [Fact]
        public void 列表行没有密钥字段()
        {
            Assert.DoesNotContain(
                typeof(ClientAppListItem).GetProperties(),
                property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void 菜单树包含停用节点_孤儿挂到根上_同级按排序()
        {
            var tree = MenuAdminTree.Build(
            [
                Menu(3, 1, "停用页", EmStatus.Disabled, sort: 2),
                Menu(2, 1, "页面", EmStatus.Enabled, sort: 1),
                Menu(1, 0, "目录", EmStatus.Enabled, sort: 0),
                Menu(9, 404, "孤儿", EmStatus.Enabled, sort: 5)
            ]);

            Assert.Equal(["目录", "孤儿"], tree.Select(node => node.Title));
            var directory = tree[0];
            Assert.Equal(["页面", "停用页"], directory.Children.Select(node => node.Title));
            Assert.Equal(EmStatus.Disabled, directory.Children[1].Status);
        }

        [Fact]
        public async Task 分页按名称和AppKey筛选_不带出密钥()
        {
            var apps = new AppProbe
            {
                Apps =
                [
                    App(1, "根控制台", "viv-rootadmin-app", "secret-a"),
                    App(2, "门店端", "shop-app", "secret-b"),
                    App(3, "别的", "other", "secret-c")
                ]
            };
            var service = new ClientAppService(new ContextProbe { UserId = 7 }, new UserProbe(Super()), apps);

            var page = await service.PageAsync(new AppPageRequest { PageIndex = 1, PageSize = 10, Keyword = "app" });

            var data = Assert.IsType<ClientAppPageOutput>(Assert.IsType<VivApiResult<ClientAppPageOutput>>(page).Data);
            Assert.Equal(2, data.TotalCount);
            Assert.Equal(["根控制台", "门店端"], data.Items.Select(item => item.Name));
            Assert.Equal(200, page.Code);
        }

        [Fact]
        public async Task 非超管不能读应用菜单()
        {
            var service = MenuServiceOf(new ContextProbe { UserId = 7 }, new UserProbe(new AtUser { Id = 7, Status = EmStatus.Enabled }));

            var result = await service.GetAdminTreeAsync(new AppMenuTreeRequest { ClientAppId = 1 });

            Assert.NotEqual(200, result.Code);
            Assert.Equal("只有超级管理员可以管理应用", result.Message);
        }

        [Fact]
        public async Task 新增菜单分配权限位_不能改到别的应用()
        {
            var menus = new MenuProbe();
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(1, "根控制台", "viv-rootadmin-app", "secret")] },
                menus);

            var first = await service.SaveForAppAsync(Save(1, 0, "目录"));
            var second = await service.SaveForAppAsync(Save(1, 0, "页面"));
            Assert.Equal(200, first.Code);
            Assert.Equal(200, second.Code);
            Assert.Equal([0, 1], menus.Rows.Select(row => row.BitIndex));

            menus.Rows.Add(new AtMenu { Id = 99, ClientAppId = 2, Title = "别人的" });
            var stolen = await service.SaveForAppAsync(Save(1, 99, "改名"));

            Assert.NotEqual(200, stolen.Code);
            Assert.Equal("菜单不存在", stolen.Message);
            Assert.Equal("别人的", menus.Rows.Single(row => row.Id == 99).Title);
        }

        [Fact]
        public async Task 有子菜单时不能删_删掉后子页面一起清()
        {
            var menus = new MenuProbe();
            menus.Rows.Add(new AtMenu { Id = 1, ClientAppId = 8, Title = "目录" });
            menus.Rows.Add(new AtMenu { Id = 2, ClientAppId = 8, ParentId = 1, Title = "页面" });
            var pages = new SubPageProbe();
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(8, "门店", "shop", "secret")] },
                menus,
                pages);

            var blocked = await service.DeleteForAppAsync(new DeleteAppMenuRequest { ClientAppId = 8, Id = 1 });
            Assert.Equal("请先删除子菜单", blocked.Message);

            var deleted = await service.DeleteForAppAsync(new DeleteAppMenuRequest { ClientAppId = 8, Id = 2 });
            Assert.Equal(200, deleted.Code);
            Assert.Equal([2L], pages.DeletedMenuIds);
            Assert.DoesNotContain(menus.Rows, row => row.Id == 2);
        }

        private static AtUser Super() => new() { Id = 7, Status = EmStatus.Enabled, IsSuperAdmin = true };

        private static AtClientApp App(long id, string name, string appKey, string secret) => new()
        {
            Id = id,
            Name = name,
            AppKey = appKey,
            AppSecret = secret,
            Status = EmStatus.Enabled,
            ClientStack = EmClientStack.Vue
        };

        private static AtMenu Menu(long id, long parentId, string title, EmStatus status, int sort) => new()
        {
            Id = id,
            ParentId = parentId,
            Title = title,
            Status = status,
            Sort = sort
        };

        private static SaveAppMenuRequest Save(long appId, long id, string title) => new()
        {
            ClientAppId = appId,
            Id = id,
            Title = title,
            Type = id == 0 ? EmMenuType.Directory : EmMenuType.Page,
            IsVisible = true,
            Status = EmStatus.Enabled
        };

        private static MenuService MenuServiceOf(
            ContextProbe context,
            UserProbe users,
            AppProbe? apps = null,
            MenuProbe? menus = null,
            SubPageProbe? pages = null)
            => new(context, users, apps ?? new AppProbe(), menus ?? new MenuProbe(), pages ?? new SubPageProbe(),
                new AdapterIndex(new VueMenuRouteAdapter()));

        private sealed class ContextProbe : IVivContext
        {
            public long UserId { get; init; }
            public long AppId => 0;
            public long SubjectId => 0;
            public string TraceId => string.Empty;
            public IVivContextAccessor VivContextAccessor => throw new NotSupportedException();
            public void SetSnapshot(VivContextContent model) { }
            public void Clear() { }
            public VivContextContent? GetRawSnapshot() => null;
        }

        private sealed class UserProbe(AtUser? user) : IUserRepository
        {
            public Task<AtUser?> GetUserAsync(long userId)
                => Task.FromResult(user is not null && user.Id == userId ? user : null);

            public Task<bool> AddUserAsync(AtUser user) => throw new NotSupportedException();
            public Task<bool> UpdateUserAsync(AtUser user) => throw new NotSupportedException();
            public Task<bool> DeleteUserAsync(long userId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteUserAsync(long userId) => throw new NotSupportedException();
            public Task<AtUser?> GetUserByPhoneAsync(string phone, EmUserType userType) => throw new NotSupportedException();
            public Task<PagedList<AtUser>> GetUserPagedListAsync(IApiPagedRequest request) => throw new NotSupportedException();
            public Task<UserBucket?> GetUserBucketAsync(long userId) => throw new NotSupportedException();
            public Task<List<AtUserRole>> GetUserRolesAsync(long userId) => throw new NotSupportedException();
            public Task<bool> AddRoleAsync(AtUserRole role) => throw new NotSupportedException();
            public Task<bool> UpdateRoleAsync(AtUserRole role) => throw new NotSupportedException();
            public Task<bool> DeleteRoleAsync(long roleId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteRoleAsync(long roleId) => throw new NotSupportedException();
            public Task<AtUserRole?> GetRoleAsync(long roleId) => throw new NotSupportedException();
            public Task<PagedList<AtUserRole>> GetRolePagedListAsync(IApiPagedRequest request) => throw new NotSupportedException();
        }

        private sealed class AppProbe : IClientAppRepository
        {
            public List<AtClientApp> Apps { get; init; } = [];

            public Task<AtClientApp?> GetAppAsync(long appId)
                => Task.FromResult(Apps.FirstOrDefault(app => app.Id == appId));

            public Task<List<AtClientApp>> ListAppsAsync() => Task.FromResult(Apps.ToList());

            public Task<bool> AddAppAsync(AtClientApp app) => throw new NotSupportedException();
            public Task<bool> UpdateAppAsync(AtClientApp app) => throw new NotSupportedException();
            public Task<bool> DeleteAppAsync(long appId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteAppAsync(long appId) => throw new NotSupportedException();
            public Task<AtClientApp?> GetAppByAppKeyAsync(string appKey) => throw new NotSupportedException();
            public Task<PagedList<AtClientApp>> GetAppPagedListAsync(IApiPagedRequest request) => throw new NotSupportedException();
            public Task<bool> AddVersionAsync(AtClientAppVersion version) => throw new NotSupportedException();
            public Task<bool> UpdateVersionAsync(AtClientAppVersion version) => throw new NotSupportedException();
            public Task<bool> DeleteVersionAsync(long versionId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteVersionAsync(long versionId) => throw new NotSupportedException();
            public Task<AtClientAppVersion?> GetVersionAsync(long versionId) => throw new NotSupportedException();
            public Task<List<AtClientAppVersion>> GetVersionsByAppAsync(long clientAppId) => throw new NotSupportedException();
            public Task<PagedList<AtClientAppVersion>> GetPagedVersionListAsync(IApiPagedRequest request) => throw new NotSupportedException();
            public Task<bool> AddNoticeAsync(AtClientAppNotice notice) => throw new NotSupportedException();
            public Task<bool> UpdateNoticeAsync(AtClientAppNotice notice) => throw new NotSupportedException();
            public Task<bool> DeleteNoticeAsync(long noticeId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteNoticeAsync(long noticeId) => throw new NotSupportedException();
            public Task<AtClientAppNotice?> GetNoticeAsync(long noticeId) => throw new NotSupportedException();
            public Task<List<AtClientAppNotice>> GetNoticesByAppAsync(long clientAppId) => throw new NotSupportedException();
            public Task<PagedList<AtClientAppNotice>> GetPagedNoticeListAsync(IApiPagedRequest request) => throw new NotSupportedException();
        }

        private sealed class MenuProbe : IMenuRepository
        {
            public List<AtMenu> Rows { get; } = [];
            private long _nextId = 1;

            public Task<List<AtMenu>> GetEnabledByAppAsync(long clientAppId)
                => Task.FromResult(Rows.Where(row => row.ClientAppId == clientAppId && row.Status == EmStatus.Enabled).ToList());

            public Task<List<AtMenu>> GetByAppAsync(long clientAppId)
                => Task.FromResult(Rows.Where(row => row.ClientAppId == clientAppId).ToList());

            public Task<int> NextBitIndexAsync(long clientAppId)
            {
                var mine = Rows.Where(row => row.ClientAppId == clientAppId).ToList();
                return Task.FromResult(mine.Count == 0 ? 0 : mine.Max(row => row.BitIndex) + 1);
            }

            public Task<AtMenu?> GetAsync(long id) => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

            public Task<long> AddAsync(AtMenu menu)
            {
                menu.Id = _nextId++;
                Rows.Add(menu);
                return Task.FromResult(menu.Id);
            }

            public Task<bool> UpdateAsync(AtMenu menu) => Task.FromResult(true);

            public Task<bool> SoftDeleteAsync(long id)
            {
                Rows.RemoveAll(row => row.Id == id);
                return Task.FromResult(true);
            }

            public Task<bool> HasChildrenAsync(long parentId)
                => Task.FromResult(Rows.Any(row => row.ParentId == parentId));
        }

        private sealed class SubPageProbe : IMenuSubPageRepository
        {
            public List<long> DeletedMenuIds { get; } = [];

            public Task<List<AtMenuSubPage>> GetByMenuIdsAsync(IReadOnlyList<long> menuIds) => Task.FromResult(new List<AtMenuSubPage>());

            public Task<bool> SoftDeleteByMenuAsync(long menuId)
            {
                DeletedMenuIds.Add(menuId);
                return Task.FromResult(true);
            }
        }

        private sealed class AdapterIndex(IMenuRouteAdapter adapter) : IIndex<EmClientStack, IMenuRouteAdapter>
        {
            public IMenuRouteAdapter this[EmClientStack key] => adapter;

            public bool TryGetValue(EmClientStack key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMenuRouteAdapter value)
            {
                value = key == adapter.Stack ? adapter : null!;
                return key == adapter.Stack;
            }
        }
    }
}
