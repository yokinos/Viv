using System.Collections.Generic;

namespace Viv.Engine
{
    /// <summary>
    /// Viv 框架运行期定义（白名单 / 常量集中存放）。
    /// </summary>
    public static class VivRunDefine
    {
        /// <summary>
        /// 请求上下文头契约：网关认证后回填、下游验签后信任。全框架跨层共用。
        /// </summary>
        public const string AppIdHeader = "x-viv-appId"; // 客户端 AppId
        public const string SubjectIdHeader = "x-viv-subjectId"; // 租户 ID = TenantId
        public const string UserIdHeader = "x-viv-userId";
        public const string ServiceNameHeader = "x-viv-serviceName"; // 服务名，如 viv.apex.api
        public const string InnerRequestTokenHeader = "x-request-token"; // 内部请求签名 Token（HMAC）

        /// <summary>
        /// 允许原样返回的 HTTP 状态码白名单。
        /// 业务在处理流程中先设置 Response.StatusCode（如 301/302 重定向、304/404 等），再返回
        /// <see cref="VivApiResult"/> 时，该状态码会被原样保留、不被强制改回 200；
        /// 不在白名单内的状态码仍按业务信封语义强制 200。
        /// </summary>
        public static readonly HashSet<int> AllowedHttpStatusCodes =
        [
            301, 302, 303, 307, 308, // 重定向（Location 由业务自行写入）
            304,                     // 缓存（If-None-Match / If-Modified-Since）
            401, 403,                // 鉴权 / 越权
            404, 405, 406, 415       // 资源不存在 / 方法不允许 / 不可接受 / 媒体类型不支持
        ];
    }
}
