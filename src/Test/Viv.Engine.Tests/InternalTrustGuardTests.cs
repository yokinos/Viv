using Viv.Contracts.Enums;
using Viv.Engine.Options;
using Viv.Engine.Power;
using Viv.Momo.Options;

namespace Viv.Engine.Tests;

[Collection("VivEngineStaticState")]
public class InternalTrustGuardTests
{
    [Fact]
    public void 生产环境缺InternalToken_启动失败()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions { Env = VivEnv.Production }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => InternalTrustGuard.Validate(options));
        Assert.Contains("InternalToken", ex.Message);
    }

    [Fact]
    public void 带数据库缺InternalToken_启动失败()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions { Env = VivEnv.Development },
            DatabaseOption = new DatabaseOptions()
        };

        var ex = Assert.Throws<InvalidOperationException>(() => InternalTrustGuard.Validate(options));
        Assert.Contains("InternalToken", ex.Message);
    }

    [Fact]
    public void 网关缺InternalToken_启动失败()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions { Env = VivEnv.Development, ServiceType = VivServiceType.Gateway }
        };

        Assert.Throws<InvalidOperationException>(() => InternalTrustGuard.Validate(options));
    }

    [Fact]
    public void Development逃生开关_允许缺密钥启动()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions
            {
                Env = VivEnv.Development,
                AllowUnsignedInternalTrust = true
            },
            DatabaseOption = new DatabaseOptions()
        };

        InternalTrustGuard.Validate(options);
    }

    [Fact]
    public void 逃生开关在生产环境_启动失败()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions
            {
                Env = VivEnv.Production,
                AllowUnsignedInternalTrust = true
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => InternalTrustGuard.Validate(options));
        Assert.Contains("AllowUnsignedInternalTrust", ex.Message);
    }

    [Fact]
    public void 配了InternalToken_直接通过()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions
            {
                Env = VivEnv.Production,
                InternalToken = "CHANGE_ME"
            },
            DatabaseOption = new DatabaseOptions()
        };

        InternalTrustGuard.Validate(options);
    }

    [Fact]
    public void Development无库无网关_不强制密钥()
    {
        var options = new VivOptions
        {
            EnvOption = new EnvOptions { Env = VivEnv.Development }
        };

        InternalTrustGuard.Validate(options);
    }
}
