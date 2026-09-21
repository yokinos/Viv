using Viv.Contracts.Enums;
using Viv.Engine.Options;

namespace Viv.Engine.Power
{
    /// <summary>
    /// 内部信任（x-viv-* / x-request-token）启动期校验。
    /// 生产 / 预发 / 测试 / 带库 / 网关 缺 InternalToken 直接启动失败；
    /// 仅 Development 允许显式 <see cref="EnvOptions.AllowUnsignedInternalTrust"/> 逃生。
    /// </summary>
    public static class InternalTrustGuard
    {
        public static void Validate(VivOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var env = options.EnvOption;
            var hatch = env?.AllowUnsignedInternalTrust == true;

            if (hatch && env!.Env != VivEnv.Development)
            {
                throw new InvalidOperationException(
                    "AllowUnsignedInternalTrust 只允许在 Development 使用。" +
                    "生产 / 预发 / 测试环境必须配置 VivOptions.EnvOption.InternalToken，禁止信任未签名的 x-viv-* 头。");
            }

            if (!string.IsNullOrWhiteSpace(env?.InternalToken))
            {
                return;
            }

            if (hatch)
            {
                return;
            }

            if (!RequiresInternalToken(options))
            {
                return;
            }

            throw new InvalidOperationException(
                "InternalToken 未配置，但当前宿主需要内部签名（生产/预发/测试、带数据库、或多租户网关）。" +
                "请设置 VivOptions.EnvOption.InternalToken（或环境变量 VivOptions__EnvOption__InternalToken）。" +
                "本地开发可在 Env=Development 时显式设置 AllowUnsignedInternalTrust=true。");
        }

        /// <summary>
        /// HTTP 是否允许在无密钥时解析未签名的 x-viv-* 身份头。
        /// 与 gRPC 默认失败闭合对齐：没配密钥就不灌上下文；只有 Development 逃生开关打开才放行。
        /// holder-id 即使走逃生也不信任。
        /// </summary>
        public static bool AllowsUnsignedHeaders()
        {
            var env = VivEngine.VivOptions?.EnvOption;
            return env?.AllowUnsignedInternalTrust == true && env.Env == VivEnv.Development;
        }

        internal static bool RequiresInternalToken(VivOptions options)
        {
            var env = options.EnvOption;
            if (env?.ServiceType == VivServiceType.Gateway) return true;
            if (options.DatabaseOption != null) return true;
            return env?.Env is VivEnv.Production or VivEnv.PreRelease or VivEnv.Test;
        }
    }
}
