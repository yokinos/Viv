using MagicOnion;
using MagicOnion.Server;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Proxy.Contracts;
using Viv.Contracts.Interface;
using Viv.Entity.Enums;

namespace Viv.Apex.Api.Rpc
{
    /// <summary>
    /// 客户端应用查询（MagicOnion 服务）。Herta.Link 建立连接前会调它确认 AppId 有效。
    /// 验签与上下文水合在框架的 <c>VivIdentityFilter</c> 里完成，这里只做业务。
    /// 验签恢复出的 AppId 若大于 0，必须和请求体里的 AppId 相同，避免拿着 A 的签名去查 B。
    /// 没配 InternalToken 时上下文 AppId 是 0，仍按请求体查（本地开发）。
    /// </summary>
    public sealed class ApexAppQueryService : ServiceBase<IApexAppQueryService>, IApexAppQueryService
    {
        private readonly IClientAppRepository _apps;
        private readonly IVivContext _context;

        public ApexAppQueryService(IClientAppRepository apps, IVivContext context)
        {
            _apps = apps;
            _context = context;
        }

        public async UnaryResult<ValidateClientAppReply> ValidateClientAppAsync(ValidateClientAppRequest request)
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

        /// <summary>
        /// 分页取客户端应用。定时任务（Coser 的 AppForeachBase）靠它遍历所有应用，
        /// 不把 AtClientApp 抄一份到别的服务。
        /// </summary>
        public async UnaryResult<GetClientAppPageReply> GetClientAppPageAsync(GetClientAppPageRequest request)
        {
            // 分页参数在这里收敛（客户端也可能不收敛），取消取自本次调用的 CallContext
            var page = await _apps.GetAppPageAsync(
                request.PageIndex,
                request.PageSize,
                keyword: null,
                Context.CallContext.CancellationToken).ConfigureAwait(false);

            var reply = new GetClientAppPageReply
            {
                TotalCount = (int)page.TotalCount,
                TotalPages = page.TotalPages
            };

            foreach (var app in page.Items)
            {
                reply.Items.Add(new ClientAppItem
                {
                    Id = app.Id,
                    AppKey = app.AppKey ?? string.Empty,
                    Name = app.Name ?? string.Empty,
                    Platform = (int)app.Platform,
                    ClientStack = (int)app.ClientStack,
                    Enabled = app.Status == EmStatus.Enabled
                });
            }

            return reply;
        }
    }
}
