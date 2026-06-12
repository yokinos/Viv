using Viv.Sdk.Rpc.Tenant;
using Viv.Sdk.Rpc.Tenant.Request;
using Viv.Sdk.Rpc.Tenant.Response;
using Viv.Sdk.Rpc.Tenant;

namespace Viv.Herta.Core.Service
{
    /// <summary>
    /// Herta 查 Tenant 信息 —— 通过 gRPC 调 Apex 的 Tenant 服务。
    /// TenantGrpcClient 由 Viv.Forge 编译时生成 DI 注册，直接注入即可。
    /// </summary>
    public class TenantIntegrationService
    {
        private readonly TenantGrpcClient _client;

        public TenantIntegrationService(TenantGrpcClient client)
        {
            _client = client;
        }

        public async Task<GetTenantResponse?> GetTenantInfoAsync(long tenantId)
        {
            return await _client.GetTenantAsync(new GetTenantRequest { TenantId = tenantId });
        }
    }
}
