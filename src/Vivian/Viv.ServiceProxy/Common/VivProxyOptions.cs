namespace Viv.ServiceProxy.Common
{
    /// <summary>
    /// 所有服务代理的配置集合。一个节点装全部下游服务，加服务只加子节点，不再往顶层堆
    /// <c>ApexProxy</c> / <c>HertaProxy</c> 这种散节点：
    /// <code>
    /// "VivProxies": {
    ///   "Apex": { "ServiceName": "viv-apex-api", "CacheSeconds": 300 }
    /// }
    /// </code>
    /// </summary>
    public sealed class VivProxyOptions
    {
        public const string SectionName = "VivProxies";

        /// <summary>key = 模块声明的短名（Apex / Herta …），大小写不敏感</summary>
        public Dictionary<string, ProxyEndpointOption> Services { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>取某个服务的配置；没配就给默认值，服务名回落模块的默认值</summary>
        public ProxyEndpointOption Resolve(string key, string defaultServiceName)
        {
            var option = Services.TryGetValue(key, out var configured) ? configured : new ProxyEndpointOption();

            if (string.IsNullOrWhiteSpace(option.ServiceName))
                option.ServiceName = defaultServiceName;

            if (option.TimeoutSeconds <= 0) option.TimeoutSeconds = 30;
            if (option.CacheSeconds < 0) option.CacheSeconds = 0;

            return option;
        }
    }
}
