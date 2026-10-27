using Autofac.Features.Indexed;
using Viv.Apex.Core.Entity.Dto.Menu;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Menu;
using Viv.Contracts.Interface;
using Viv.Elysia.Request;
using Viv.Engine;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    /// <summary>
    /// 菜单服务。读：取数据、挑适配器、交给适配器组装；写：校验归属与技术栈，再落库。
    /// 路由结构由适配器决定（Vue 是嵌套树，别的端可能是平铺），服务不参与。
    /// </summary>
    public class MenuService : IMenuService
    {
        private readonly IVivContext _context;
        private readonly IClientAppRepository _clientApps;
        private readonly IMenuRepository _menus;
        private readonly IMenuSubPageRepository _subPages;
        private readonly IIndex<EmClientStack, IMenuRouteAdapter> _adapters;

        public MenuService(IVivContext context, IClientAppRepository clientApps, IMenuRepository menus, IMenuSubPageRepository subPages,
            IIndex<EmClientStack, IMenuRouteAdapter> adapters)
        {
            _context = context;
            _clientApps = clientApps;
            _menus = menus;
            _subPages = subPages;
            _adapters = adapters;
        }

        public async Task<VivApiResult> GetRouteTreeAsync()
        {
            var resolved = await ResolveAsync();
            if (resolved.Error is not null) return resolved.Error;

            var menus = await _menus.GetEnabledByAppAsync(resolved.AppId);
            var pages = menus.Count == 0
                ? []
                : await _subPages.GetByMenuIdsAsync(menus.Select(x => x.Id).ToList());

            return VivApiResult<object>.Success(resolved.Adapter!.BuildTree(menus, pages));
        }

        public async Task<VivApiResult> SaveAsync(SaveMenuRequest request)
        {
            var resolved = await ResolveAsync();
            if (resolved.Error is not null) return resolved.Error;

            var title = request.Title?.Trim();
            if (string.IsNullOrEmpty(title))
                return VivApiResult.Failed("菜单名称不能为空");

            // 路由配置必须能被当前技术栈解释，否则坏 JSON 要到客户端渲染时才暴露
            if (!resolved.Adapter!.IsValidConfig(request.RouteConfig))
                return VivApiResult.Failed("路由配置格式不正确");

            if (request.ParentId < 0)
                return VivApiResult.Failed("父菜单不合法");

            AtMenu? existing = null;
            if (request.Id > 0)
            {
                existing = await _menus.GetAsync(request.Id);
                if (existing is null || existing.ClientAppId != resolved.AppId)
                    return VivApiResult.Failed("菜单不存在");

                if (request.ParentId == request.Id)
                    return VivApiResult.Failed("不能把自己设为父菜单");

                if (await IsDescendantAsync(request.ParentId, request.Id))
                    return VivApiResult.Failed("不能挂到自己的子菜单下");
            }

            if (request.ParentId > 0)
            {
                var parent = await _menus.GetAsync(request.ParentId);
                if (parent is null || parent.ClientAppId != resolved.AppId)
                    return VivApiResult.Failed("父菜单不存在");
            }

            if (existing is null)
            {
                await _menus.AddAsync(new AtMenu
                {
                    ClientAppId = resolved.AppId,
                    ParentId = request.ParentId,
                    Title = title,
                    Type = request.Type,
                    Icon = request.Icon,
                    Sort = request.Sort,
                    IsVisible = request.IsVisible,
                    RouteConfig = request.RouteConfig,
                    Description = request.Description,
                    Status = request.Status
                });
            }
            else
            {
                existing.ParentId = request.ParentId;
                existing.Title = title;
                existing.Type = request.Type;
                existing.Icon = request.Icon;
                existing.Sort = request.Sort;
                existing.IsVisible = request.IsVisible;
                existing.RouteConfig = request.RouteConfig;
                existing.Description = request.Description;
                existing.Status = request.Status;

                await _menus.UpdateAsync(existing);
            }

            return VivApiResult.Success();
        }

        public async Task<VivApiResult> DeleteAsync(ApiIdRequest request)
        {
            var resolved = await ResolveAsync();
            if (resolved.Error is not null) return resolved.Error;

            var menu = await _menus.GetAsync(request.Id);
            if (menu is null || menu.ClientAppId != resolved.AppId)
                return VivApiResult.Failed("菜单不存在");

            // 有子菜单就先删子菜单，避免删完父节点留下挂不上的孤儿节点
            if (await _menus.HasChildrenAsync(request.Id))
                return VivApiResult.Failed("请先删除子菜单");

            await _subPages.SoftDeleteByMenuAsync(request.Id);
            await _menus.SoftDeleteAsync(request.Id);

            return VivApiResult.Success();
        }

        /// <summary>菜单属于当前客户端应用，应用取到了、适配器也取到了才算就绪</summary>
        private async Task<(long AppId, IMenuRouteAdapter? Adapter, VivApiResult? Error)> ResolveAsync()
        {
            var appId = _context.AppId;
            if (appId <= 0)
                return (0, null, VivApiResult.Failed("缺少客户端标识"));

            var app = await _clientApps.GetAppAsync(appId);
            if (app is null || app.Status != EmStatus.Enabled)
                return (appId, null, VivApiResult.Failed("客户端不存在或已停用"));

            // 适配器按客户端技术栈注册（keyed），这里按当前应用挑一个
            if (!_adapters.TryGetValue(app.ClientStack, out var adapter) || adapter is null)
                return (appId, null, VivApiResult.Failed("该客户端没有对应的菜单适配器"));

            return (appId, adapter, null);
        }

        /// <summary>candidateParentId 是不是 menuId 的后代（防止把父级挂到自己下面成环）</summary>
        private async Task<bool> IsDescendantAsync(long candidateParentId, long menuId)
        {
            var current = candidateParentId;
            var guard = 0;

            while (current > 0 && guard++ < 100)
            {
                if (current == menuId) return true;

                var parent = await _menus.GetAsync(current);
                if (parent is null) return false;

                current = parent.ParentId;
            }

            return false;
        }
    }
}
