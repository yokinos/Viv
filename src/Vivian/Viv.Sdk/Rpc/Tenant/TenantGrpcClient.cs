using Viv.Echo.Grpc;
using Viv.Sdk.Rpc.Tenant.Request;
using Viv.Sdk.Rpc.Tenant.Response;

namespace Viv.Sdk.Rpc.Tenant
{
    /// <summary>
    /// Tenant 服务的 gRPC 客户端。<br/>
    /// 由 protoc 从 .proto 生成，加一行 [GrpcClient] 即可。
    /// </summary>
    [GrpcClient("tenant", "https://apex.km.com")]
    public class TenantGrpcClient
    {
        public Task<GetTenantResponse> GetTenantAsync(GetTenantRequest request)
        {
            throw new NotImplementedException("由 proto 生成");
        }
    }
}
