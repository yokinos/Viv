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

            var data = Assert.IsType<PagedList<ClientAppListItem>>(Assert.IsType<VivApiResult<PagedList<ClientAppListItem>>>(page).Data);
            Assert.Equal(2, data.TotalCount);
            Assert.Equal(["根控制台", "门店端"], data.Items.Select(item => item.Name));
            Assert.Equal(200, page.Code);
        }

        [Fact]
        public async Task 非超管不能保存应用()
        {
            var service = new ClientAppService(new ContextProbe { UserId = 7 }, new UserProbe(new AtUser { Id = 7, Status = EmStatus.Enabled }), new AppProbe());

            var result = await service.SaveAsync(new SaveAppRequest { Name = "门店", AppKey = "shop" });

            Assert.NotEqual(200, result.Code);
        }

        [Fact]
        public async Task 新增应用生成密钥_更新不改密钥也不再下发()
        {
            var apps = new AppProbe();
            var service = new ClientAppService(new ContextProbe { UserId = 7 }, new UserProbe(Super()), apps);

            var created = await service.SaveAsync(new SaveAppRequest { Name = "门店端", AppKey = "shop-app" });
            var createdData = Assert.IsType<SaveAppOutput>(Assert.IsType<VivApiResult<SaveAppOutput>>(created).Data);
            Assert.False(string.IsNullOrWhiteSpace(createdData.AppSecret));

            var updated = await service.SaveAsync(new SaveAppRequest
            {
                Id = createdData.Id,
                Name = "门店端改名",
                AppKey = "shop-app"
            });
            var updatedData = Assert.IsType<SaveAppOutput>(Assert.IsType<VivApiResult<SaveAppOutput>>(updated).Data);
            Assert.Null(updatedData.AppSecret);
            Assert.Equal(createdData.AppSecret, apps.Apps.Single().AppSecret);
            Assert.Equal("门店端改名", apps.Apps.Single().Name);
        }

        [Fact]
        public async Task AppKey重复时拒绝保存()
        {
            var apps = new AppProbe { Apps = [App(1, "根控制台", "viv-rootadmin-app", "secret-a")] };
            var service = new ClientAppService(new ContextProbe { UserId = 7 }, new UserProbe(Super()), apps);

            var result = await service.SaveAsync(new SaveAppRequest { Name = "另一个", AppKey = "viv-rootadmin-app" });

            Assert.NotEqual(200, result.Code);
            Assert.Equal("AppKey 已被使用", result.Message);
        }

        [Fact]
        public async Task 非超管不能读菜单分页()
        {
            var service = MenuServiceOf(new ContextProbe { UserId = 7 }, new UserProbe(new AtUser { Id = 7, Status = EmStatus.Enabled }));

            var result = await service.GetAdminPageAsync(new AppMenuPageRequest { ClientAppId = 1 });

            Assert.NotEqual(200, result.Code);
            Assert.Equal("无权操作", result.Message);
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

        [Fact]
        public async Task 新增动作分配权限位_只允许页面菜单_编辑不改位()
        {
            var menus = new MenuProbe();
            menus.Rows.Add(new AtMenu { Id = 1, ClientAppId = 8, Title = "目录", Type = EmMenuType.Directory });
            menus.Rows.Add(new AtMenu { Id = 2, ClientAppId = 8, Title = "页面", Type = EmMenuType.Page });
            var actions = new ActionProbe();
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(8, "门店", "shop", "secret"), App(9, "别的", "other", "secret")] },
                menus,
                actions: actions);

            var onDirectory = await service.SaveActionForAppAsync(new SaveAppMenuActionRequest { ClientAppId = 8, MenuId = 1, Name = "删除" });
            Assert.Equal("只有页面菜单可以维护动作", onDirectory.Message);

            var first = await service.SaveActionForAppAsync(new SaveAppMenuActionRequest { ClientAppId = 8, MenuId = 2, Name = "新增", Sort = 1, Status = EmStatus.Enabled });
            var second = await service.SaveActionForAppAsync(new SaveAppMenuActionRequest { ClientAppId = 8, MenuId = 2, Name = "删除", Sort = 2, Status = EmStatus.Enabled });
            Assert.Equal(200, first.Code);
            Assert.Equal(200, second.Code);
            Assert.Equal([0, 1], actions.Rows.Select(row => row.BitIndex));

            var updated = await service.SaveActionForAppAsync(new SaveAppMenuActionRequest
            {
                ClientAppId = 8,
                Id = 1,
                MenuId = 2,
                Name = "新增改名",
                Sort = 9,
                Status = EmStatus.Disabled
            });
            Assert.Equal(200, updated.Code);
            var row = actions.Rows.Single(item => item.Id == 1);
            Assert.Equal("新增改名", row.Name);
            Assert.Equal(0, row.BitIndex);

            // 动作归属按「动作 -> 菜单 -> 应用」校验，换个应用不能改
            var stolen = await service.SaveActionForAppAsync(new SaveAppMenuActionRequest { ClientAppId = 9, Id = 1, MenuId = 2, Name = "偷改" });
            Assert.NotEqual(200, stolen.Code);
        }

        [Fact]
        public async Task 删除菜单时动作一起清()
        {
            var menus = new MenuProbe();
            menus.Rows.Add(new AtMenu { Id = 2, ClientAppId = 8, Title = "页面", Type = EmMenuType.Page });
            var actions = new ActionProbe();
            actions.Rows.Add(new AtMenuAction { Id = 21, MenuId = 2, Name = "删除", Status = EmStatus.Enabled });
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(8, "门店", "shop", "secret")] },
                menus,
                actions: actions);

            var deleted = await service.DeleteForAppAsync(new DeleteAppMenuRequest { ClientAppId = 8, Id = 2 });

            Assert.Equal(200, deleted.Code);
            Assert.Equal([2L], actions.DeletedMenuIds);
            Assert.Empty(actions.Rows);
        }

        [Fact]
        public async Task 菜单分页带子页面和动作()
        {
            var menus = new MenuProbe();
            menus.Rows.Add(new AtMenu { Id = 1, ClientAppId = 8, Title = "页面", Type = EmMenuType.Page, Sort = 1 });
            menus.Rows.Add(new AtMenu { Id = 2, ClientAppId = 8, Title = "另一个", Type = EmMenuType.Directory, Sort = 2 });
            var pages = new SubPageProbe();
            pages.Rows.Add(new AtMenuSubPage { Id = 11, MenuId = 1, Title = "子页", Sort = 1, IsVisible = true, Status = EmStatus.Enabled });
            var actions = new ActionProbe();
            actions.Rows.Add(new AtMenuAction { Id = 21, MenuId = 1, Name = "删除", Sort = 1, Status = EmStatus.Enabled });
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(8, "门店", "shop", "secret")] },
                menus,
                pages,
                actions);

            var result = await service.GetAdminPageAsync(new AppMenuPageRequest { ClientAppId = 8, PageIndex = 1, PageSize = 10 });

            var data = Assert.IsType<PagedList<AppMenuRow>>(Assert.IsType<VivApiResult<PagedList<AppMenuRow>>>(result).Data);
            Assert.Equal(2, data.TotalCount);
            // 先按类型（目录在前），再按 ParentId、排序号
            var items = data.Items.ToList();
            Assert.Equal(["另一个", "页面"], items.Select(item => item.Title));
            Assert.Equal("子页", items[1].SubPages.Single().Title);
            Assert.Equal("删除", items[1].Actions.Single().Name);
            Assert.Empty(items[0].SubPages);

            var filtered = await service.GetAdminPageAsync(new AppMenuPageRequest { ClientAppId = 8, Keyword = "另一" });
            var filteredData = Assert.IsType<PagedList<AppMenuRow>>(Assert.IsType<VivApiResult<PagedList<AppMenuRow>>>(filtered).Data);
            Assert.Equal(1, filteredData.TotalCount);
            Assert.Equal("另一个", filteredData.Items.Single().Title);
        }

        [Fact]
        public async Task 新增子页面分配权限位_只允许页面_编辑不改位()
        {
            var menus = new MenuProbe();
            menus.Rows.Add(new AtMenu { Id = 1, ClientAppId = 8, Title = "目录", Type = EmMenuType.Directory });
            menus.Rows.Add(new AtMenu { Id = 2, ClientAppId = 8, Title = "页面", Type = EmMenuType.Page });
            var pages = new SubPageProbe();
            var service = MenuServiceOf(
                new ContextProbe { UserId = 7 },
                new UserProbe(Super()),
                new AppProbe { Apps = [App(8, "门店", "shop", "secret")] },
                menus,
                pages);

            var onDirectory = await service.SaveSubPageForAppAsync(new SaveAppMenuSubPageRequest { ClientAppId = 8, MenuId = 1, Title = "子页" });
            Assert.Equal("只有页面菜单可以维护子页面", onDirectory.Message);

            var first = await service.SaveSubPageForAppAsync(new SaveAppMenuSubPageRequest { ClientAppId = 8, MenuId = 2, Title = "列表", Sort = 1 });
            var second = await service.SaveSubPageForAppAsync(new SaveAppMenuSubPageRequest { ClientAppId = 8, MenuId = 2, Title = "详情", Sort = 2 });
            Assert.Equal(200, first.Code);
            Assert.Equal(200, second.Code);
            Assert.Equal([0, 1], pages.Rows.Select(row => row.BitIndex));

            var updated = await service.SaveSubPageForAppAsync(new SaveAppMenuSubPageRequest
            {
                ClientAppId = 8,
                Id = 1,
                MenuId = 2,
                Title = "列表改名",
                Sort = 9,
                IsVisible = false
            });
            Assert.Equal(200, updated.Code);
            var row = pages.Rows.Single(item => item.Id == 1);
            Assert.Equal("列表改名", row.Title);
            Assert.Equal(0, row.BitIndex);
            Assert.False(row.IsVisible);

            var deleted = await service.DeleteSubPageForAppAsync(new DeleteAppMenuSubPageRequest { ClientAppId = 8, Id = 1 });
            Assert.Equal(200, deleted.Code);
            Assert.Equal([1L], pages.DeletedIds);
            Assert.Single(pages.Rows);
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
            SubPageProbe? pages = null,
            ActionProbe? actions = null)
            => new(context, users, apps ?? new AppProbe(), menus ?? new MenuProbe(), pages ?? new SubPageProbe(),
                actions ?? new ActionProbe(), new AdapterIndex(new VueMenuRouteAdapter()));

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

            public Task<bool> AddAppAsync(AtClientApp app)
            {
                Apps.Add(app);
                return Task.FromResult(true);
            }

            public Task<bool> UpdateAppAsync(AtClientApp app)
            {
                var index = Apps.FindIndex(item => item.Id == app.Id);
                if (index < 0) return Task.FromResult(false);
                Apps[index] = app;
                return Task.FromResult(true);
            }

            public Task ForgetAppKeyAsync(string? appKey) => Task.CompletedTask;
            public Task<bool> DeleteAppAsync(long appId) => throw new NotSupportedException();
            public Task<bool> SoftDeleteAppAsync(long appId) => throw new NotSupportedException();
            public Task<AtClientApp?> GetAppByAppKeyAsync(string appKey)
                => Task.FromResult(Apps.FirstOrDefault(app => app.AppKey == appKey));
            public Task<PagedList<AtClientApp>> GetAppPagedListAsync(IApiPagedRequest request)
            {
                var keyword = (request as AppPageRequest)?.Keyword?.Trim();
                var query = Apps.AsEnumerable();
                if (!string.IsNullOrEmpty(keyword))
                {
                    query = query.Where(app =>
                        (app.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (app.AppKey ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                var ordered = query.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ThenBy(app => app.Id).ToList();
                var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
                var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

                return Task.FromResult(new PagedList<AtClientApp>(pageIndex, pageSize)
                {
                    TotalCount = ordered.Count,
                    Items = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList()
                });
            }
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

            public Task<PagedList<AppMenuRow>> GetPagedAsync(IApiPagedRequest request)
            {
                var concrete = request as AppMenuPageRequest;
                var clientAppId = concrete?.ClientAppId ?? 0;
                var keyword = concrete?.Keyword?.Trim();

                var rows = Rows.Where(row => row.ClientAppId == clientAppId).ToList();
                if (!string.IsNullOrEmpty(keyword))
                {
                    rows = rows
                        .Where(row => (row.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                var titles = Rows.ToDictionary(row => row.Id, row => row.Title ?? string.Empty);
                var childCounts = Rows
                    .Where(row => row.ParentId > 0)
                    .GroupBy(row => row.ParentId)
                    .ToDictionary(group => group.Key, group => group.Count());

                var ordered = rows
                    .OrderBy(row => row.Type)
                    .ThenBy(row => row.ParentId)
                    .ThenBy(row => row.Sort)
                    .ThenBy(row => row.Id)
                    .ToList();
                var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
                var pageSize = request.PageSize < 1 ? 20 : request.PageSize;
                var items = ordered
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .Select(row => new AppMenuRow
                    {
                        Id = row.Id,
                        ParentId = row.ParentId,
                        ParentTitle = row.ParentId > 0 && titles.TryGetValue(row.ParentId, out var parentTitle)
                            ? parentTitle
                            : string.Empty,
                        Title = row.Title ?? string.Empty,
                        Type = row.Type,
                        Icon = row.Icon,
                        Sort = row.Sort,
                        IsVisible = row.IsVisible,
                        RouteConfig = row.RouteConfig,
                        Description = row.Description,
                        Status = row.Status,
                        ChildCount = childCounts.GetValueOrDefault(row.Id)
                    })
                    .ToList();

                return Task.FromResult(new PagedList<AppMenuRow>(pageIndex, pageSize)
                {
                    TotalCount = ordered.Count,
                    Items = items
                });
            }

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
            public List<AtMenuSubPage> Rows { get; } = [];
            public List<long> DeletedMenuIds { get; } = [];
            public List<long> DeletedIds { get; } = [];
            private long _nextId = 1;

            public Task<List<AtMenuSubPage>> GetByMenuIdsAsync(IReadOnlyList<long> menuIds) => Task.FromResult(new List<AtMenuSubPage>());

            public Task<List<AtMenuSubPage>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds)
                => Task.FromResult(Rows.Where(row => menuIds.Contains(row.MenuId)).ToList());

            public Task<AtMenuSubPage?> GetAsync(long id)
                => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

            public Task<int> NextBitIndexAsync(long menuId)
            {
                var mine = Rows.Where(row => row.MenuId == menuId).ToList();
                return Task.FromResult(mine.Count == 0 ? 0 : mine.Max(row => row.BitIndex) + 1);
            }

            public Task<long> AddAsync(AtMenuSubPage page)
            {
                page.Id = _nextId++;
                Rows.Add(page);
                return Task.FromResult(page.Id);
            }

            public Task<bool> UpdateAsync(AtMenuSubPage page)
            {
                var index = Rows.FindIndex(row => row.Id == page.Id);
                if (index < 0) return Task.FromResult(false);
                Rows[index] = page;
                return Task.FromResult(true);
            }

            public Task<bool> SoftDeleteAsync(long id)
            {
                DeletedIds.Add(id);
                Rows.RemoveAll(row => row.Id == id);
                return Task.FromResult(true);
            }

            public Task<bool> SoftDeleteByMenuAsync(long menuId)
            {
                DeletedMenuIds.Add(menuId);
                Rows.RemoveAll(row => row.MenuId == menuId);
                return Task.FromResult(true);
            }
        }

        private sealed class ActionProbe : IMenuActionRepository
        {
            public List<AtMenuAction> Rows { get; } = [];
            public List<long> DeletedMenuIds { get; } = [];
            private long _nextId = 1;

            public Task<List<AtMenuAction>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds)
                => Task.FromResult(Rows.Where(row => menuIds.Contains(row.MenuId)).ToList());

            public Task<AtMenuAction?> GetAsync(long id)
                => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

            public Task<int> NextBitIndexAsync(long menuId)
            {
                var mine = Rows.Where(row => row.MenuId == menuId).ToList();
                return Task.FromResult(mine.Count == 0 ? 0 : mine.Max(row => row.BitIndex) + 1);
            }

            public Task<long> AddAsync(AtMenuAction action)
            {
                action.Id = _nextId++;
                Rows.Add(action);
                return Task.FromResult(action.Id);
            }

            public Task<bool> UpdateAsync(AtMenuAction action)
            {
                var index = Rows.FindIndex(row => row.Id == action.Id);
                if (index < 0) return Task.FromResult(false);
                Rows[index] = action;
                return Task.FromResult(true);
            }

            public Task<bool> SoftDeleteAsync(long id)
            {
                Rows.RemoveAll(row => row.Id == id);
                return Task.FromResult(true);
            }

            public Task<bool> SoftDeleteByMenuAsync(long menuId)
            {
                DeletedMenuIds.Add(menuId);
                Rows.RemoveAll(row => row.MenuId == menuId);
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
