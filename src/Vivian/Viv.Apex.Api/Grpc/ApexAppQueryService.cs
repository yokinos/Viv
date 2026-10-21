using Grpc.Core;
using Viv.Apex.Core.IRepository;
using Viv.Entity.Enums;
using Viv.ServiceProxy.Protos;

namespace Viv.Apex.Api.Grpc
{
    /// <summary>
    /// 客户端应用查询。Herta.Link 建立连接前会调它确认 AppId 有效，
    /// 靠 VivGrpcDiscovery 自动注册，不需要在 Program.cs 里手工挂。
    /// </summary>
    public class ApexAppQueryService : ApexAppQuery.ApexAppQueryBase
    {
        private readonly IClientAppRepository _apps;

        public ApexAppQueryService(IClientAppRepository apps)
        {
            _apps = apps;
        }

        public override async Task<ValidateClientAppReply> ValidateClientApp(
            ValidateClientAppRequest request,
            ServerCallContext context)
        {
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
