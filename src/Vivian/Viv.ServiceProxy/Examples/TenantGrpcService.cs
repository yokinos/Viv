using Grpc.Core;
using Viv.Contracts.Interface;
using Viv.ServiceProxy.Protos;
using Protos = Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Examples
{
    /// <summary>
    /// 框架级 gRPC 能力示例实例（自包含，不依赖业务）。
    ///
    /// 演示核心：租户上下文已由 <see cref="Viv.Echo.Grpc.VivGrpcServerInterceptor"/> 从请求头 x-viv-* 恢复，
    /// 服务实现可直接注入 scoped <see cref="IVivContext"/> 读取 AppId / SubjectId / UserId——
    /// 与 HTTP 控制器侧 VivContextMiddleware 水合后的用法一致。
    ///
    /// 宿主映射：<c>builder.RunVivApi(app =&gt; app.MapGrpcService&lt;TenantGrpcService&gt;());</c>
    /// </summary>
    public class TenantGrpcService : Protos.TenantGrpcService.TenantGrpcServiceBase
    {
        private readonly IVivContext _vivContext;

        public TenantGrpcService(IVivContext vivContext)
        {
            _vivContext = vivContext;
        }

        public override async Task<GetTenantResponse> GetTenant(GetTenantRequest request, ServerCallContext context)
        {
            // request.SubjectId == 0 时回落请求上下文里的租户（由拦截器从 x-viv-subjectId 恢复）
            var subjectId = request.SubjectId > 0 ? request.SubjectId : _vivContext.SubjectId;
            var tenant = new TenantInfo
            {
                SubjectId = subjectId,
                SubjectName = $"租户{subjectId}",
                AppId = _vivContext.AppId,
                UserId = _vivContext.UserId
            };

            return new GetTenantResponse { Success = true, Message = "ok", Tenant = tenant };
        }

        public override async Task ListTenantUsers(ListTenantUsersRequest request, IServerStreamWriter<UserInfo> responseStream, ServerCallContext context)
        {
            for (var i = 1; i <= 3; i++)
            {
                await responseStream.WriteAsync(new UserInfo { UserId = i, UserName = $"用户{i}" });
            }
        }

        public override async Task<UploadTenantUsersResponse> UploadTenantUsers(IAsyncStreamReader<UserInfo> requestStream, ServerCallContext context)
        {
            var count = 0;
            await foreach (var user in requestStream.ReadAllAsync())
            {
                count++;
            }

            return new UploadTenantUsersResponse { Count = count };
        }

        public override async Task Chat(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream, ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new ChatMessage { UserId = message.UserId, Content = $"echo:{message.Content}" });
            }
        }
    }
}
