namespace Viv.ServiceProxy.Apex
{
    /// <summary>Apex 客户端应用查询的接入配置。</summary>
    public sealed class ApexClientAppOption
    {
        /// <summary>Apex 的 gRPC 地址，或 Aspire 服务名。</summary>
        public string ServiceName { get; set; } = "viv-apex-api";

        /// <summary>是否按服务名走 Aspire 服务发现（<c>services__*</c> 环境变量）。</summary>
        public bool UseServiceDiscovery { get; set; } = true;

        /// <summary>校验结果的进程内缓存秒数。Apex 禁用客户端后最多这么久生效。</summary>
        public int CacheSeconds { get; set; } = 300;
    }
}
