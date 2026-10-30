using Grpc.Core;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Entity.Enums;
using Viv.ServiceProxy.Protos;

namespace Viv.Apex.Api.Grpc
{
    /// <summary>
    /// 客户端应用查询。Herta.Link 建立连接前会调它确认 AppId 有效，
    /// 靠 VivGrpcDiscovery 自动注册，不需要在 Program.cs 里手工挂。
    /// 验签恢复出的 AppId 若大于 0，必须和请求体里的 AppId 相同，避免拿着 A 的签名去查 B。
    /// 没配 InternalToken 时上下文 AppId 是 0，仍按请求体查（本地开发）。
    /// </summary>
    public class ApexAppQueryService : ApexAppQuery.ApexAppQueryBase
    {
        private readonly IClientAppRepository _apps;
        private readonly IVivContext _context;

        public ApexAppQueryService(IClientAppRepository apps, IVivContext context)
        {
            _apps = apps;
            _context = context;
        }

        public override async Task<ValidateClientAppReply> ValidateClientApp(
            ValidateClientAppRequest request,
            ServerCallContext context)
        {
            if (_context.AppId > 0 && _context.AppId != request.AppId)
            {
                return new ValidateClientAppReply
                {
                    Exists = false,
                    Message = "客户端与签名身份不一致"
                };
            }

            var app = await _apps.GetAppAsync(request.AppId).ConfigureAwait(false);

            if (app == null)
                return new ValidateClientAppReply { Exists = false, Message = "客户端不存在" };

            if (app.Status != EmStatus.Enabled)
                return new ValidateClientAppReply
                {
                    Exists = true,
                    Enabled = false,
                    Name = app.Name ?? string.Empty,
                    Message = "客户端已禁用"
                };

            return new ValidateClientAppReply
            {
                Exists = true,
                Enabled = true,
                Name = app.Name ?? string.Empty
            };
        }
    }
}
