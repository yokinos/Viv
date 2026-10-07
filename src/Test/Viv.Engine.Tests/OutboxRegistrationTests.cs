using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Enums;
using Viv.Engine.Options;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Outbox;
using Viv.Outbox.Options;

namespace Viv.Engine.Tests;

/// <summary>
/// 发件箱装配对 <c>NanaOption</c> 的取舍：入队只需要业务主库（走 <c>ExecuteSqlAsync</c> 并入调用方事务），
/// MQ 只有**投递**才需要。所以「配了 OutboxOption 就必须配 NanaOption」这条硬校验只对真的跑投递器的宿主成立。
///
/// 只写不投的宿主（<c>EnableDispatcher = false</c>，把投递交给 Worker）是真实存在的形态：
/// Viv.Ouroboros.Api 就是 —— 它写发件箱，但配 MqttOption 之外的 MQ 宿主纯属多余。
/// 早先那条校验会把这类宿主直接拦在启动外。
/// </summary>
public class OutboxRegistrationTests
{
    /// <summary>带库 + 有 InternalToken，越过 InternalTrustGuard 这道与发件箱无关的前置校验。</summary>
    private static VivOptions Options(bool enableDispatcher, bool withNana)
    {
        return new VivOptions
        {
            EnvOption = new EnvOptions
            {
                InternalToken = "00000000000000000000000000000000",
                Env = VivEnv.Development,
            },
            DatabaseOption = new DatabaseOptions
            {
                DatabaseSource = DatabaseSourceType.SqlServer,
                MasterConnectionString = "server=localhost;database=viv_test;user id=sa;password=x",
            },
            OutboxOption = new OutboxOptions { EnableDispatcher = enableDispatcher },
            NanaOption = withNana
                ? new NanaOptions { Host = "localhost", Port = 5672, UserName = "guest", Password = "guest" }
                : null,
        };
    }

    [Fact]
    public void 只写不投的宿主_没有NanaOption也能装配出IVivOutbox()
    {
        var services = new ServiceCollection();

        services.AddViv(Options(enableDispatcher: false, withNana: false));

        Assert.Contains(services, x => x.ServiceType == typeof(IVivOutbox));
    }

    [Fact]
    public void 跑投递器却没有NanaOption_启动即抛而不是投递时才炸()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<Exception>(() => services.AddViv(Options(enableDispatcher: true, withNana: false)));

        Assert.Contains("NanaOption", ex.Message);
    }

    [Fact]
    public void 跑投递器且有NanaOption_正常装配()
    {
        var services = new ServiceCollection();

        services.AddViv(Options(enableDispatcher: true, withNana: true));

        Assert.Contains(services, x => x.ServiceType == typeof(IVivOutbox));
    }
}
