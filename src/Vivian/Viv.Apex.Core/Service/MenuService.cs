using Autofac.Features.Indexed;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Menu;
using Viv.Contracts.Interface;
using Viv.Entity.Enums;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    /// <summary>
    /// 菜单服务。职责：取数据、挑适配器、交给适配器组装。
    /// 路由结构由适配器决定（Vue 是嵌套树，别的端可能是平铺），服务不参与。
    /// </summary>
    public class MenuService : IMenuService
    {
        private readonly IVivContext _context;
        private readonly IClientAppRepository _clientApps;
        private readonly IMenuRepository _menus;
        private readonly IMenuSubPageRepository _subPages;
        private readonly IIndex<EmClientStack, IMenuRouteAdapter> _adapters;

        public MenuService(
            IVivContext context,
            IClientAppRepository clientApps,
            IMenuRepository menus,
            IMenuSubPageRepository subPages,
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
            var appId = _context.AppId;
            if (appId <= 0)
                return VivApiResult.Failed("缺少客户端标识");

            var app = await _clientApps.GetAppAsync(appId);
            if (app is null || app.Status != EmStatus.Enabled)
                return VivApiResult.Failed("客户端不存在或已停用");

            // 适配器按客户端技术栈注册（keyed），这里按当前应用挑一个
            if (!_adapters.TryGetValue(app.ClientStack, out var adapter) || adapter is null)
                return VivApiResult.Failed("该客户端没有对应的菜单适配器");

            var menus = await _menus.GetEnabledByAppAsync(appId);
            var pages = menus.Count == 0
                ? []
                : await _subPages.GetByMenuIdsAsync(menus.Select(x => x.Id).ToList());

            return VivApiResult<object>.Success(adapter.BuildTree(menus, pages));
        }
    }
}
