namespace Viv.Keke.Proxies
{
    /// <summary>
    /// 单个下游服务的代理配置。每个 <see cref="IVivRpcProxyModule"/> 从
    /// <c>VivProxies:&lt;Key&gt;</c> 节点独立绑定一份（直接绑定到本类型，不再经过字典包装 ——
    /// 旧的 <c>VivProxyOptions.Services</c> 字典写法 Binder 绑不上，是死配置）。
    /// </summary>
    public sealed class VivRpcClientOption
    {
        /// <summary>
        /// 下游服务名（Aspire 名，如 <c>viv-apex-api</c>）。不填时用模块声明的默认值。
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// 走 Aspire 服务发现：地址解析成 <c>http://_grpc.{ServiceName}</c>。地址能写死时关掉。
        /// </summary>
        public bool UseServiceDiscovery { get; set; } = true;

        /// <summary>调用超时（秒）。内部调用必须有上限，否则下游卡住会拖死调用方。</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 传输选择。默认 <see cref="VivRpcTransport.Grpc"/>：迁移期不配就走现状，零行为变化。
        /// </summary>
        public VivRpcTransport Transport { get; set; } = VivRpcTransport.Grpc;

        /// <summary>超时的时间跨度（已做下限保护）。</summary>
        public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 30 : TimeoutSeconds);
    }
}
