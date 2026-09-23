using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Viv.Engine.HealthChecks;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Engine.Tests;

/// <summary>
/// 健康检查的立场是「说结论」而不是「把异常甩出去」—— 甩出去整个 /health 变 500，
/// 反而看不出是哪一项挂了。这里用必然探不动的输入钉住这一点，不连真 Redis / 真库。
///
/// Redis 那条依赖 RedisFactory 的静态初始化状态：进程里没人 Initialize 过它，
/// GetConnectionAsync 就会同步抛 InvalidOperationException（Viv.Engine.Tests 全程不碰真 Redis）。
/// </summary>
public class HealthCheckTests
{
    [Fact]
    public async Task Redis探不动时返回Unhealthy而不是抛异常()
    {
        var result = await new RedisHealthCheck().CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task 未配主库连接串时返回Unhealthy()
    {
        var check = new DatabaseHealthCheck(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions()));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("未配置主库连接字符串", result.Description);
    }

    [Fact]
    public async Task 库类型不支持时返回Unhealthy而不是抛异常()
    {
        var options = new DatabaseOptions
        {
            MasterConnectionString = "server=x;database=viv_test",
            DatabaseSource = (DatabaseSourceType)99
        };
        var check = new DatabaseHealthCheck(Microsoft.Extensions.Options.Options.Create(options));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }
}
