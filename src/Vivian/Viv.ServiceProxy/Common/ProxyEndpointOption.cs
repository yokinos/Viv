namespace Viv.ServiceProxy.Common
{
    /// <summary>单个下游服务的代理配置。都有默认值，宿主不配也能跑</summary>
    public sealed class ProxyEndpointOption
    {
        /// <summary>
        /// Aspire 服务名。不填时用模块声明的默认值（如 Apex → viv-apex-api）
        /// </summary>
        public string? ServiceName { get; set; }

        public bool UseServiceDiscovery { get; set; } = true;

        /// <summary>HTTP 调用超时（秒）。内部调用必须有上限，否则依赖卡住会拖死调用方</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>准入类查询的结果缓存秒数；分页/写操作不缓存</summary>
        public int CacheSeconds { get; set; } = 300;
    }
}
