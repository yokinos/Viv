using Grpc.Core;
using Viv.Contracts.Interface;
using Viv.ServiceProxy.Tests.Protos;

namespace Viv.ServiceProxy.Tests.Grpc
{
    /// <summary>
    /// gRPC 测试桩：演示租户上下文已由 <see cref="Viv.Echo.Grpc.VivGrpcServerInterceptor"/>
    /// 从请求头 x-viv-* 恢复，实现里直接注入 scoped <see cref="IVivContext"/> 就能读。
    ///
    /// 放在测试项目里而不是产品程序集：`VivGrpcDiscovery` 会自动发现并映射所有实现，
    /// 留在 ServiceProxy 里等于给每个引用它的宿主挂一个假数据服务。
    /// </summary>
    public class TenantGrpcStub : Protos.TenantGrpcService.TenantGrpcServiceBase
    {
        private readonly IVivContext _vivContext;

        public TenantGrpcStub(IVivContext vivContext)
        {
            _vivContext = vivContext;
        }

        public override Task<GetTenantResponse> GetTenant(GetTenantRequest request, ServerCallContext context)
        {
            // request.SubjectId == 0 时回落请求上下文里的租户（由拦截器从 x-viv-subjectId 恢复）
            var subjectId = request.SubjectId > 0 ? request.SubjectId : _vivContext.SubjectId;

            return Task.FromResult(new GetTenantResponse
            {
                Success = true,
                Message = "ok",
                Tenant = new TenantInfo
                {
                    SubjectId = subjectId,
                    SubjectName = $"租户{subjectId}",
                    AppId = _vivContext.AppId,
                    UserId = _vivContext.UserId
                }
            });
        }

        public override async Task ListTenantUsers(
            ListTenantUsersRequest request,
            IServerStreamWriter<UserInfo> responseStream,
            ServerCallContext context)
        {
            for (var i = 1; i <= 3; i++)
            {
                await responseStream.WriteAsync(new UserInfo { UserId = i, UserName = $"用户{i}" });
            }
        }

        public override async Task<UploadTenantUsersResponse> UploadTenantUsers(
            IAsyncStreamReader<UserInfo> requestStream,
            ServerCallContext context)
        {
            var count = 0;
            await foreach (var _ in requestStream.ReadAllAsync())
            {
                count++;
            }

            return new UploadTenantUsersResponse { Count = count };
        }

        public override async Task Chat(
            IAsyncStreamReader<ChatMessage> requestStream,
            IServerStreamWriter<ChatMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new ChatMessage
                {
                    UserId = message.UserId,
                    Content = $"echo:{message.Content}"
                });
            }
        }
    }
}
