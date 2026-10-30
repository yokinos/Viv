using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Access;
using Viv.Contracts.Interface;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class ClientAppService : IClientAppService
    {
        private readonly IVivContext _context;
        private readonly IUserRepository _users;
        private readonly IClientAppRepository _apps;

        public ClientAppService(IVivContext context, IUserRepository users, IClientAppRepository apps)
        {
            _context = context;
            _users = users;
            _apps = apps;
        }

        public async Task<VivApiResult> PageAsync(AppPageRequest request)
        {
            var denied = await SuperAdminGate.DenyAsync(_context, _users);
            if (denied is not null) return denied;

            var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
            var pageSize = request.PageSize < 1 ? 20 : Math.Min(request.PageSize, 100);

            var apps = await _apps.ListAppsAsync();
            var keyword = request.Keyword?.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                apps = apps.Where(app =>
                        Contains(app.Name, keyword) || Contains(app.AppKey, keyword))
                    .ToList();
            }

            var ordered = apps
                .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(app => app.Id)
                .ToList();

            var items = ordered
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(ClientAppListItem.From)
                .ToList();

            return VivApiResult<ClientAppPageOutput>.Success(new ClientAppPageOutput
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                TotalCount = ordered.Count,
                Items = items
            });
        }

        private static bool Contains(string? value, string keyword)
            => value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
