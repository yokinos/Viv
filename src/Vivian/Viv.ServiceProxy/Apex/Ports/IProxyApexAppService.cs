namespace Viv.ServiceProxy.Apex.Ports
{
    /// <summary>
    /// Apex 应用能力的远程入口。注入它就能调 Apex，gRPC 细节（地址、验签头、序列化、缓存）
    /// 全在实现里，调用方看不到。<br/>
    /// 命名约定：<c>IProxy{服务}{能力}Service</c> —— <c>IProxy</c> 前缀提醒这是跨网络调用
    /// （有延迟、可能失败、参数要可序列化），别当本地服务随便循环调。
    /// </summary>
    public interface IProxyApexAppService
    {
        /// <summary>
        /// 这个客户端应用能不能用（存在且启用）。失败按"不可用"处理，不抛异常 —— 准入路径不能因为
        /// 依赖挂了就放行。
        /// </summary>
        Task<AppCheckResult> CheckAsync(long clientAppId, CancellationToken cancellationToken = default);

        /// <summary>分页取应用清单。失败会记日志后抛给调用方，由批处理决定重试还是终止</summary>
        Task<AppPage> GetAppPageAsync(AppPageRequest request, CancellationToken cancellationToken = default);
    }
}
