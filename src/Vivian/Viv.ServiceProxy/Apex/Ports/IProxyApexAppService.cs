namespace Viv.ServiceProxy.Apex.Ports
{
    /// <summary>
    /// Apex 应用查询的远程入口。地址、签名头、缓存都在实现里。
    /// 命名按 IProxy{服务}{能力}Service，前缀表示这条调用走网络。
    /// </summary>
    public interface IProxyApexAppService
    {
        /// <summary>应用是否可用（存在且启用）。依赖不可用时按拒绝处理，不抛异常</summary>
        Task<AppCheckResult> CheckAsync(long clientAppId, CancellationToken cancellationToken = default);

        /// <summary>分页取应用。失败先记日志再抛，由调用方决定重试还是终止</summary>
        Task<AppPage> GetAppPageAsync(AppPageRequest request, CancellationToken cancellationToken = default);
    }
}
