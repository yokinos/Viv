using Microsoft.Extensions.Configuration;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 测试环境 —— 控制 RequestTokenResolver 密钥来源（VivEngine.VivOptions 静态状态）。
/// VivEngine.VivOptions 只能经 LoadVivConfig 设置且无重置 API，测试通过 in-memory IConfiguration 切换密钥模式。
/// </summary>
internal static class EngineTestEnv
{
    /// <summary>强制回落模式：VivOptions 无 InternalToken，密钥走 VivConfigRegistry 的 TokenOptions。</summary>
    public static void ForceFallbackMode()
        => VivEngine.LoadVivConfig(new ConfigurationBuilder().Build());

    /// <summary>写入指定 InternalToken 并加载，验证 EnvOption 优先级。</summary>
    public static void ForceEnvTokenMode(string internalToken)
        => VivEngine.LoadVivConfig(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VivOptions:EnvOption:InternalToken"] = internalToken
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
