namespace Viv.Aspire.AppHost;

/// <summary>
/// 注册中心 — 定义基础设施资源（Postgres / Redis / RabbitMQ）
/// 参数值在 appsettings.json 的 Parameters 节点或环境变量中配置
/// </summary>
public static class RegisterCenter
{
    public static InfrastructureParams Register(IDistributedApplicationBuilder builder)
    {
        // 添加 Aspire 连接资源，使 Redis 和 RabbitMQ 在仪表板中可见
        var redisResource = builder.AddConnectionString("redis");
        var rabbitMqResource = builder.AddConnectionString("rabbitmq");

        return new InfrastructureParams
        {
            PostgresConnection = builder.AddParameter("PostgresConnection"),
            RedisConnection = builder.AddParameter("RedisConnection"),
            RabbitMqHost = builder.AddParameter("RabbitMqHost"),
            RabbitMqPort = builder.AddParameter("RabbitMqPort"),
            RabbitMqUser = builder.AddParameter("RabbitMqUser"),
            RabbitMqPassword = builder.AddParameter("RabbitMqPassword"),
            RabbitMqVirtualHost = builder.AddParameter("RabbitMqVirtualHost"),
            RedisResource = redisResource,
            RabbitMqResource = rabbitMqResource,
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

    /// <summary>
    /// Redis 连接资源（仪表板可见）
    /// </summary>
    public IResourceBuilder<IResourceWithConnectionString> RedisResource { get; set; } = default!;

    /// <summary>
    /// RabbitMQ 连接资源（仪表板可见）
    /// </summary>
    public IResourceBuilder<IResourceWithConnectionString> RabbitMqResource { get; set; } = default!;
}
