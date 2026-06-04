using System.IO;
using Aspire.Hosting;

namespace Viv.Aspire.AppHost;

public static class RegisterCenter
{
    private const string AspiresParameterJsonFileName = "viv.aspireparameter.json";
    private const string ParameterKeyName = "AspireParameter";

    /// <summary>
    /// 集中注册基础设施资源
    /// </summary>
    public static InfrastructureParams Register(IDistributedApplicationBuilder builder)
    {
        // 1. 安全读取配置文件（防止文件不存在导致 AppHost 启动崩溃）
        string parameterJson = string.Empty;
        if (File.Exists(AspiresParameterJsonFileName))
        {
            parameterJson = File.ReadAllText(AspiresParameterJsonFileName);
        }

        // 2. 注册连接字符串，使其在 Aspire Dashboard 中可见且支持本地/云端自动切换
        var redisResource = builder.AddConnectionString("RedisService");
        var rabbitMqResource = builder.AddConnectionString("RabbitMqService");

        // 3. 注册自定义参数（secret: true 可以在仪表盘上隐藏输入并安全存储到 User Secrets）
        var aspresParam = builder.AddParameter(ParameterKeyName, parameterJson, secret: false);

        return new InfrastructureParams
        {
            RedisResource = redisResource,
            RabbitMqResource = rabbitMqResource,
            AspresParameterResource = aspresParam
        };
    }

    /// <summary>
    /// 扩展方法：将 AspireParameter 注入到依赖该参数的子项目中
    /// </summary>
    public static IResourceBuilder<T> AddVivParameter<T>(this IResourceBuilder<T> builder, IResourceBuilder<ParameterResource> parameterResource)
        where T : IResourceWithEnvironment
    {
        // 使用 WithEnvironment 注入时，Aspire 会自动处理 ParameterResource 的值解析
        return builder.WithEnvironment(ParameterKeyName, parameterResource);
    }
}

public class InfrastructureParams
{
    public IResourceBuilder<ParameterResource> AspresParameterResource { get; set; } = default!;
    public IResourceBuilder<IResourceWithConnectionString> RedisResource { get; set; } = default!;
    public IResourceBuilder<IResourceWithConnectionString> RabbitMqResource { get; set; } = default!;
}