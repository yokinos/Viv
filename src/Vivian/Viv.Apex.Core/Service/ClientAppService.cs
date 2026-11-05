using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Access;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion.Generic;
using Viv.Delusion.Magic;
using Viv.Elysia.Request;
using Viv.Engine;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    public class ClientAppService : IClientAppService
    {
        private readonly IVivContext _context;
        private readonly IUserRepository _userRepository;
        private readonly IClientAppRepository _clientAppRepository;

        public ClientAppService(IVivContext context, IUserRepository userRepository, IClientAppRepository clientAppRepository)
        {
            _context = context;
            _userRepository = userRepository;
            _clientAppRepository = clientAppRepository;
        }

        public async Task<VivApiResult> GetPageAsync(AppPageRequest request)
        {
            var denied = await SuperAdminGate.DenyAsync(_context, _userRepository);
            if (denied is not null) return denied;

            var paged = await _clientAppRepository.GetAppPagedListAsync(request);

            return VivApiResult.Success(paged);
        }

        public async Task<VivApiResult> GetAppAsync(ApiIdRequest request)
        {
            var denied = await SuperAdminGate.DenyAsync(_context, _userRepository);
            if (denied is not null) return denied;

            var app = await _clientAppRepository.GetAppAsync(request.Id);
            if (app is null)
                return VivApiResult.Failed("客户端不存在");

            return VivApiResult<ClientAppListItem>.Success(ClientAppListItem.From(app));
        }

        //[VivDistributedLock("app")]
        public async Task<VivApiResult> SaveAsync(SaveAppRequest request)
        {
            var denied = await SuperAdminGate.DenyAsync(_context, _userRepository);
            if (denied is not null) return denied;

            var name = request.Name.Trim();
            var appKey = request.AppKey.Trim();

            var occupied = await _clientAppRepository.GetAppByAppKeyAsync(appKey);
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
                await _clientAppRepository.AddAppAsync(created);
                return VivApiResult<SaveAppOutput>.Success(new SaveAppOutput
                {
                    Id = created.Id,
                    AppSecret = created.AppSecret
                });
            }

            var existing = await _clientAppRepository.GetAppAsync(request.Id);
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
            await _clientAppRepository.UpdateAppAsync(existing);
            if (!string.Equals(previousKey, appKey, StringComparison.Ordinal))
                await _clientAppRepository.ForgetAppKeyAsync(previousKey);

            return VivApiResult<SaveAppOutput>.Success(new SaveAppOutput { Id = existing.Id });
        }

        private static string? BlankToNull(string? value)
        {
            var text = value?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
