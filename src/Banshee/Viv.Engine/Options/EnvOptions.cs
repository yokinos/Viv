using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Enums;

namespace Viv.Engine.Options
{
    public class EnvOptions
    {
        public VivEnv Env { get; set; }

        public string? ServiceName { get; set; }

        public int MachineId { get; set; }

        public VivServiceType ServiceType { get; set; }

        /// <summary>
        /// 内部请求共享签名密钥（x-request-token HMAC-SHA256）。
        /// 网关与所有服务必须配置同一个值；不再回落到 TokenOptions.SecretKey。
        /// 生产 / 预发 / 测试 / 带库宿主缺它会启动失败，见 <c>InternalTrustGuard</c>。
        /// </summary>
        public string? InternalToken { get; set; }

        /// <summary>
        /// 本地开发逃生：允许缺 InternalToken 启动，并且 HTTP 信任未签名的 x-viv-* 身份头
        /// （holder-id 仍然不信）。只允许 <see cref="VivEnv.Development"/>；其它环境启动即抛。
        /// </summary>
        public bool AllowUnsignedInternalTrust { get; set; }

        /// <summary>
        /// 信任的转发代理（IP 或 CIDR）。空 = 清空 ASP.NET 默认的 loopback 限制，
        /// 让 Aspire/YARP 容器网关的 X-Forwarded-* 生效。生产要收紧时再填。
        /// </summary>
        public string[]? TrustedProxies { get; set; }
    }
}
