using System;
using Viv.Contracts.Enums;
using Viv.Engine.Options;

namespace Viv.Engine.Power
{
    /// <summary>
    /// 内部信任（x-viv-* / x-request-token）启动期校验。
    /// 生产 / 预发 / 测试 / 带库 / 网关 缺 InternalToken 直接启动失败；
    /// 仅「宿主环境与 <see cref="EnvOptions.Env"/> 同时为 Development」才允许
    /// <see cref="EnvOptions.AllowUnsignedInternalTrust"/> 逃生。
    ///
    /// <para>
    /// 为什么两个条件都要：入库的 appsettings.json 里 <c>EnvOption.Env</c> 写的是 0(Development)，
    /// 生产容器照搬配置时该值仍是 Development；只看它等于把「本地逃生开关」带到线上。
    /// 宿主环境（ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT）由部署决定，不会跟着配置文件走。
    /// </para>
    /// </summary>
    public static class InternalTrustGuard
    {
        /// <param name="isHostDevelopment">
        /// 宿主是否 Development（<c>IHostEnvironment.IsDevelopment()</c>）。传 null 时读
        /// ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT，与 Host 默认规则一致。
        /// 测试显式传值，避免依赖进程环境变量。
        /// </param>
        public static void Validate(VivOptions options, bool? isHostDevelopment = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            var env = options.EnvOption;
            var hatch = env?.AllowUnsignedInternalTrust == true;
            var hostIsDev = isHostDevelopment ?? ReadHostIsDevelopment();

            if (hatch && (env!.Env != VivEnv.Development || !hostIsDev))
            {
                throw new InvalidOperationException(
                    "AllowUnsignedInternalTrust 只允许在 Development 使用（EnvOption.Env 与宿主环境都必须是 Development）。" +
                    "生产 / 预发 / 测试环境必须配置 VivOptions.EnvOption.InternalToken，禁止信任未签名的 x-viv-* 头。" +
                    "入库的 appsettings.json 里 Env=0 不代表宿主是开发环境，部署时请用 ASPNETCORE_ENVIRONMENT 明确指定。");
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
        /// <param name="isHostDevelopment">同 <see cref="Validate"/>；null 时读环境变量。</param>
        public static bool AllowsUnsignedHeaders(bool? isHostDevelopment = null)
        {
            var env = VivEngine.VivOptions?.EnvOption;
            if (env?.AllowUnsignedInternalTrust != true || env.Env != VivEnv.Development)
            {
                return false;
            }

            return isHostDevelopment ?? ReadHostIsDevelopment();
        }

        internal static bool RequiresInternalToken(VivOptions options)
        {
            var env = options.EnvOption;
            if (env?.ServiceType == VivServiceType.Gateway) return true;
            if (options.DatabaseOption != null) return true;
            return env?.Env is VivEnv.Production or VivEnv.PreRelease or VivEnv.Test;
        }

        /// <summary>
        /// 读宿主环境。WebApplication 用 ASPNETCORE_ENVIRONMENT，泛型 Host / Worker 允许 DOTNET_ENVIRONMENT；
        /// 未设置时 ASP.NET Core 按 Production 处理，这里同样返回 false（失败闭合）。
        /// </summary>
        private static bool ReadHostIsDevelopment()
        {
            var name = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                       ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            return string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase);
        }
    }
}
