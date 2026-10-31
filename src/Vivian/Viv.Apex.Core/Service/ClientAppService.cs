using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Access;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Engine;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

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

            var paged = await _apps.GetAppPagedListAsync(request);

            return VivApiResult<ClientAppPageOutput>.Success(new ClientAppPageOutput
            {
                PageIndex = paged.PageIndex,
                PageSize = paged.PageSize,
                TotalCount = paged.TotalCount,
                Items = paged.Items.Select(ClientAppListItem.From).ToList()
            });
        }

        public async Task<VivApiResult> SaveAsync(SaveAppRequest request)
        {
            var denied = await SuperAdminGate.DenyAsync(_context, _users);
            if (denied is not null) return denied;

            var name = request.Name.Trim();
            var appKey = request.AppKey.Trim();

            var occupied = await _apps.GetAppByAppKeyAsync(appKey);
            if (occupied is not null && occupied.Id != request.Id)
                return VivApiResult.Failed("AppKey 已被使用");

            if (request.Id <= 0)
            {
                var created = new AtClientApp
                {
                    Id = IdMagic.NextId(),
                    Name = name,
                    AppKey = appKey,
                    AppSecret = Guid.NewGuid().ToString("N"),
                    Platform = request.Platform,
                    ClientStack = request.ClientStack,
                    Source = request.Source,
                    Status = request.Status,
                    PackageName = BlankToNull(request.PackageName),
                    BundleId = BlankToNull(request.BundleId),
                    Remark = BlankToNull(request.Remark)
                };
                await _apps.AddAppAsync(created);
                return VivApiResult<SaveAppOutput>.Success(new SaveAppOutput
                {
                    Id = created.Id,
                    AppSecret = created.AppSecret
                });
            }

            var existing = await _apps.GetAppAsync(request.Id);
            if (existing is null)
                return VivApiResult.Failed("应用不存在");

            var previousKey = existing.AppKey;
            existing.Name = name;
            existing.AppKey = appKey;
            existing.Platform = request.Platform;
            existing.ClientStack = request.ClientStack;
            existing.Source = request.Source;
            existing.Status = request.Status;
            existing.PackageName = BlankToNull(request.PackageName);
            existing.BundleId = BlankToNull(request.BundleId);
            existing.Remark = BlankToNull(request.Remark);
            await _apps.UpdateAppAsync(existing);
            if (!string.Equals(previousKey, appKey, StringComparison.Ordinal))
                await _apps.ForgetAppKeyAsync(previousKey);

            return VivApiResult<SaveAppOutput>.Success(new SaveAppOutput { Id = existing.Id });
        }

        private static string? BlankToNull(string? value)
        {
            var text = value?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
