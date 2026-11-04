namespace Viv.ServiceProxy
{
    /// <summary>单个下游服务的代理配置，都有默认值</summary>
    public sealed class ProxyEndpointOption
    {
        /// <summary>Aspire 服务名，不填用模块给的默认值</summary>
        public string? ServiceName { get; set; }

        public bool UseServiceDiscovery { get; set; } = true;

        /// <summary>调用超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>准入结果的缓存秒数，分页查询不用它</summary>
        public int CacheSeconds { get; set; } = 300;
    }
}
