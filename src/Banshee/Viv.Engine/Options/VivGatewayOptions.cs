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
    }
}
