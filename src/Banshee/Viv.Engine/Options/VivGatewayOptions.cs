using System;
using System.Collections.Generic;

namespace Viv.Engine.Options
{
    /// <summary>网关自身配置（对应 appsettings.json 的 VivGateway 节点）</summary>
    public class VivGatewayOptions
    {
        public const string SectionName = "VivGateway";

        /// <summary>
        /// 服务名 → 网关短名。配了就用配置，没配才按服务名推断。
        /// 例：{ "viv-herta-link": "hertalink" } → /hertalink/api/...
        /// </summary>
        public Dictionary<string, string> ShortNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 下游请求的活动超时（秒），主要给 SSE / 长回答用，默认 1800。
        /// YARP 默认 100 秒，长流会被掐断；调小可以更快回收卡死的下游连接。
        /// 配 &lt;= 0 用默认值。
        ///
        /// <para>
        /// 注意：YARP 的 ActivityTimeout 挂在 **cluster** 上，无法按路由区分是否流式。
        /// 所以这个值对同一服务的 api / docs / ws 全部生效，普通 API 也会吃 30 分钟
        /// （代价是悬挂的下游连接最长占这么久，而不是 YARP 默认的 100 秒）。
        /// </para>
        /// </summary>
        public int StreamActivityTimeoutSeconds { get; set; } = 1800;
    }
}
