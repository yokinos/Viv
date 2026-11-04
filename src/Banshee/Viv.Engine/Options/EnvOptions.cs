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

        /// <summary>
        /// 显示名称，OpenAPI/Scalar 的标题用。不配就回落 <see cref="ServiceName"/>
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// 网关路径前缀（如 apex → /apex/api/...）。只用于本服务的 OpenAPI 文档展示，
        /// 不参与路由生成 —— 网关的短名在它的 VivGateway:ShortNames 里配
        /// </summary>
        public string? ShortName { get; set; }

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
        /// 档位密钥（OtModelProfile.ApiKeyCipher）的专用 AES 主钥匙。**与 InternalToken 解耦**：
        /// InternalToken 明文写在入库的 appsettings.json 里，拿它加密等于"能读仓库就能解全部档位密钥"，
        /// 且轮换它会把已加密的档位密钥一起作废。本项只放不入库的文件（appsettings.Development.json）
        /// 或环境变量 VivOptions__EnvOption__AiKeySecret；不配则回退 InternalToken 并在启动/首次使用时告警。
        /// 换钥匙后用 <c>POST api/ModelProfiles/reencrypt?force=true</c> 把已有密文迁移过来。
        /// </summary>
        public string? AiKeySecret { get; set; }

        /// <summary>
        /// 信任的转发代理（IP 或 CIDR）。**配置后**用它整体替换 ASP.NET 默认的 loopback 白名单，
        /// X-Forwarded-For/Proto/Host 只接受这些来源；配错/写空串不会退化成「信任所有人」。
        ///
        /// <para>
        /// 不配时按宿主环境分档：Development 清空白名单（Aspire 动态端口、局域网机器名都能用）；
        /// 其他环境保留 ASP.NET 默认（只信 loopback）。容器 / 反向代理部署在非 Development 下
        /// 必须配这里（例如 Docker 网段 <c>172.16.0.0/12</c>），否则转发头会被丢弃，客户端 IP
        /// 退化成代理 IP（限流会按代理 IP 分区，等于所有人共用一个桶）。
        /// </para>
        /// </summary>
        public string[]? TrustedProxies { get; set; }

        /// <summary>
        /// 是否由服务自身做 HTTP → HTTPS 跳转。**默认 false**。
        ///
        /// 本架构由网关 / 边缘节点终止 TLS：网关用 http 连下游，下游一旦自己跳转，
        /// 浏览器会被 307 到服务自己的 https 端口（如 <c>https://localhost:7093</c>），
        /// 请求直接逃出网关、连同 <c>/{服务短名}</c> 前缀一起丢掉。
        /// 只有当某个服务被**直接**以 http 暴露、且需要它自己跳到 https 时才打开。
        /// </summary>
        public bool EnableHttpsRedirection { get; set; }
    }
}
