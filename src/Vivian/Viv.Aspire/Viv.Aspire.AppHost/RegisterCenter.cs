namespace Viv.Aspire.AppHost;

/// <summary>
/// 注册中心 — 通过 AddParameter 定义基础设施连接参数
/// 参数值在 appsettings.json 的 Parameters 节点或环境变量中配置
/// </summary>
public static class RegisterCenter
{
    public static InfrastructureParams Register(IDistributedApplicationBuilder builder)
    {
        return new InfrastructureParams
        {
            PostgresConnection = builder.AddParameter("PostgresConnection"),
            RedisConnection = builder.AddParameter("RedisConnection"),
            RabbitMqHost = builder.AddParameter("RabbitMqHost"),
            RabbitMqPort = builder.AddParameter("RabbitMqPort"),
            RabbitMqUser = builder.AddParameter("RabbitMqUser"),
            RabbitMqPassword = builder.AddParameter("RabbitMqPassword"),
            RabbitMqVirtualHost = builder.AddParameter("RabbitMqVirtualHost"),
        };
    }
}

public class InfrastructureParams
{
    public IResourceBuilder<ParameterResource> PostgresConnection { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RedisConnection { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RabbitMqHost { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RabbitMqPort { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RabbitMqUser { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RabbitMqPassword { get; set; } = default!;
    public IResourceBuilder<ParameterResource> RabbitMqVirtualHost { get; set; } = default!;
}
