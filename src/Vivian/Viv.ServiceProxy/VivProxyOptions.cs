namespace Viv.ServiceProxy
{
    /// <summary>
    /// 下游服务代理的配置集合，挂在 VivProxies 节点下：
    /// </summary>
    public sealed class VivProxyOptions
    {
        public const string SectionName = "VivProxies";

        /// <summary>key 是模块声明的短名，大小写不敏感</summary>
        public Dictionary<string, ProxyEndpointOption> Services { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>取某个服务的配置，缺的项填默认值</summary>
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
