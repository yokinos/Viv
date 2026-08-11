using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 测试环境 —— 控制 RequestTokenResolver 密钥来源（VivEngine.VivOptions 静态状态）。
/// VivEngine.VivOptions 只能经 LoadVivConfig 设置且无重置 API，测试通过加载不同的临时配置切换密钥模式。
/// </summary>
internal static class EngineTestEnv
{
    private static readonly string FallbackPath = Path.Combine(Path.GetTempPath(), "viv-engine-empty-" + Guid.NewGuid().ToString("N") + ".json");
    private static readonly string EnvTokenPath = Path.Combine(Path.GetTempPath(), "viv-engine-envtoken-" + Guid.NewGuid().ToString("N") + ".json");

    /// <summary>强制回落模式：VivOptions 无 InternalToken，密钥走 VivConfigRegistry 的 TokenOptions。</summary>
    public static void ForceFallbackMode()
    {
        File.WriteAllText(FallbackPath, "{}");
        VivEngine.LoadVivConfig(FallbackPath);
    }

    /// <summary>写入指定 InternalToken 并加载，验证 EnvOption 优先级。</summary>
    public static void ForceEnvTokenMode(string internalToken)
    {
        File.WriteAllText(EnvTokenPath, $"{{\"EnvOption\":{{\"InternalToken\":\"{internalToken}\"}}}}");
        VivEngine.LoadVivConfig(EnvTokenPath);
    }
}
