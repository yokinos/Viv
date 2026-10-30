var builder = DistributedApplication.CreateBuilder(args);

var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api")
    .WithVivGrpcEndpoint(7001)
    .WithDeveloperCertificateTrust(true);

var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api")
    // 登录要问 Apex 这个 AppId 是不是有效客户端（gRPC）
    .WithVivGrpcEndpoint(7002)
    .WithReference(apexApi)
    .WithDeveloperCertificateTrust(true);

var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link")
    // 连接准入要问 Apex 这个 AppId 是不是有效客户端（gRPC），不引用就拿不到服务发现地址
    .WithReference(apexApi)
    .WithDeveloperCertificateTrust(true);

var deepRedApi = builder.AddProject<Projects.Viv_DeepRed_Api>("viv-deepred-api")
    .WithDeveloperCertificateTrust(true);

var sakumaiApi = builder.AddProject<Projects.Viv_SakuMai_Api>("viv-sakumai-api")
    .WithDeveloperCertificateTrust(true);

var ouroborosApi = builder.AddProject<Projects.Viv_Ouroboros_Api>("viv-ouroboros-api")
    .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
    // 固定对外端口：前端（viv-master-admin）要写死地址，动态端口每次启动都变
    .WithHttpEndpoint(port: 5262, name: "http")
    .WithReference(apexApi)
    .WithReference(hertaApi)
    .WithReference(hertaLink)
    .WithReference(deepRedApi)
    .WithReference(sakumaiApi)
    // 网关路由按服务发现构建，不 WithReference 就找不到这个服务（之前漏了 Ouroboros）
    .WithReference(ouroborosApi)
    .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Apex_Worker>("viv-apex-worker");

builder.AddProject<Projects.Viv_DeepRed_Worker>("viv-deepred-worker");

builder.AddProject<Projects.Viv_Ouroboros_Worker>("viv-ouroboros-worker");

builder.Build().Run();

/// <summary>
/// gRPC 单独端口，名字必须是 grpc，客户端才会解析 http://_grpc.{服务名}。
/// env 把监听端口写进 GrpcOption.Port，同时让 Aspire 不要把这个端点塞进 ASPNETCORE_URLS / Kestrel__Endpoints
/// （TargetPortEnvironmentVariable 一旦有值，ShouldInjectEndpointEnvironment 直接跳过）。
/// 否则 AddVivGrpcKestrel 会把同一端口再绑成 HTTP/1.1，和这里的 HTTP/2 监听冲突。
/// isProxied: false：明文 h2c 不走 DCP 的 HTTP 代理。
/// </summary>
internal static class AppHostGrpcEndpoint
{
    public static IResourceBuilder<T> WithVivGrpcEndpoint<T>(this IResourceBuilder<T> builder, int port)
        where T : IResourceWithEndpoints
    {
        return builder
            .WithHttpEndpoint(
                port: port,
                targetPort: port,
                name: "grpc",
                env: "VivOptions__EchoOption__GrpcOption__Port",
                isProxied: false)
            .WithEndpoint("grpc", endpoint => endpoint.Transport = "http2");
    }
}
