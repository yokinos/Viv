using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.Grpc;
using Protos = Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Examples
{
    /// <summary>
    /// gRPC 客户端用法示例（示意，不运行）。
    ///
    /// 1) DI 注册 —— 显式地址：
    /// <code>
    /// builder.Services.AddVivGrpcClient&lt;Protos.TenantGrpcService.TenantGrpcServiceClient&gt;("http://localhost:5000");
    /// </code>
    ///    或 Aspire 服务发现（按服务名解析 services__* 环境变量，AppHost WithReference 注入）：
    /// <code>
    /// builder.Services.AddVivGrpcClient&lt;Protos.TenantGrpcService.TenantGrpcServiceClient&gt;("viv-apex-api", useServiceDiscovery: true);
    /// </code>
    ///
    /// 2) 注入 Protos.TenantGrpcService.TenantGrpcServiceClient 直接调用——
    ///    VivGrpcInterceptor 自动把当前租户上下文（x-viv-appId / x-viv-subjectId / x-viv-userId）注入请求头，
    ///    服务端 VivGrpcServerInterceptor 据此恢复租户上下文。
    /// </summary>
    public static class TenantGrpcClientDemo
    {
        public static void Register(IServiceCollection services)
        {
            services.AddVivGrpcClient<Protos.TenantGrpcService.TenantGrpcServiceClient>("http://localhost:5000");
        }

        public static async Task CallAsync(Protos.TenantGrpcService.TenantGrpcServiceClient client)
        {
            // 空请求：服务端取请求上下文 SubjectId（客户端拦截器已透传）
            var response = await client.GetTenantAsync(new Protos.GetTenantRequest());
            _ = response.Tenant?.SubjectName;
        }
    }
}
