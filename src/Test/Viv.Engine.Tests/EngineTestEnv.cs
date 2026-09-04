using Microsoft.Extensions.Configuration;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 测试环境 —— 控制 RequestTokenResolver 密钥来源（VivEngine.VivOptions 静态状态）。
/// 密钥只走 EnvOption.InternalToken，不再回落 TokenOptions.SecretKey。
/// </summary>
internal static class EngineTestEnv
{
    /// <summary>清空 InternalToken（无法签名）。</summary>
    public static void ForceFallbackMode()
        => VivEngine.LoadVivConfig(new ConfigurationBuilder().Build());

    /// <summary>写入指定 InternalToken 并加载。serviceType 默认 WebApi（下游采纳 holder）；Gateway 一律自生成。</summary>
    public static void ForceEnvTokenMode(string internalToken, int serviceType = 0)
        => VivEngine.LoadVivConfig(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VivOptions:EnvOption:InternalToken"] = internalToken,
                ["VivOptions:EnvOption:ServiceType"] = serviceType.ToString()
            })
            .Build());
}

/// <summary>
/// 静态共享状态测试的禁用并行集合：RequestTokenResolverTests / VivConfigBindingTests 都改 VivEngine.VivOptions，
/// 跨类并行会互相污染，强制串行。
/// </summary>
[CollectionDefinition("VivEngineStaticState", DisableParallelization = true)]
public sealed class VivEngineStaticStateCollection
{
}
