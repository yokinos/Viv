using Microsoft.Extensions.Configuration;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 测试环境 —— 控制 RequestTokenResolver 密钥来源（VivEngine.VivOptions 静态状态）。
/// 密钥只走 EnvOption.InternalToken，不再回落 TokenOptions.SecretKey。
/// </summary>
internal static class EngineTestEnv
{
    private const string HostEnvVar = "ASPNETCORE_ENVIRONMENT";

    /// <summary>清空 InternalToken（无法签名）。</summary>
    public static void ForceFallbackMode()
    {
        // 宿主环境回到「未设置」（ASP.NET Core 视为 Production），避免上一个用例把逃生开关开着的状态带过来。
        SetHostEnvironment(null);
        VivConfigLoader.Load(new ConfigurationBuilder().Build());
    }

    /// <summary>写入指定 InternalToken 并加载。serviceType 默认 WebApi（下游采纳 holder）；Gateway 一律自生成。</summary>
    public static void ForceEnvTokenMode(string internalToken, int serviceType = 0)
    {
        SetHostEnvironment(null);
        VivConfigLoader.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VivOptions:EnvOption:InternalToken"] = internalToken,
                ["VivOptions:EnvOption:ServiceType"] = serviceType.ToString()
            })
            .Build());
    }

    /// <summary>
    /// Development + AllowUnsignedInternalTrust：HTTP 允许解析未签名身份头（holder-id 仍不信）。
    /// 逃生开关要求 <c>EnvOption.Env</c> 与宿主环境同时为 Development，这里两样都摆好。
    /// </summary>
    public static void ForceUnsignedHatchMode()
    {
        SetHostEnvironment("Development");
        VivConfigLoader.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VivOptions:EnvOption:Env"] = "0",
                ["VivOptions:EnvOption:AllowUnsignedInternalTrust"] = "true"
            })
            .Build());
    }

    private static void SetHostEnvironment(string? value)
        => Environment.SetEnvironmentVariable(HostEnvVar, value);
}

/// <summary>
/// 静态共享状态测试的禁用并行集合：RequestTokenResolverTests / VivConfigBindingTests 都改 VivEngine.VivOptions，
/// 跨类并行会互相污染，强制串行。
/// </summary>
[CollectionDefinition("VivEngineStaticState", DisableParallelization = true)]
public sealed class VivEngineStaticStateCollection
{
}
