namespace Viv.ServiceProxy.Apex
{
    /// <summary>
    /// 客户端应用是否可用。权威数据在 Apex 的 AtClientApp，这里只定义访问口，
    /// gRPC 实现在同项目 <see cref="ApexClientAppQuery"/>。
    /// </summary>
    public interface IClientAppQuery
    {
        Task<ClientAppCheck> ValidateAsync(long appId, CancellationToken cancellationToken = default);
    }

    /// <summary>校验结果。不允许时 <paramref name="Reason"/> 直接写进日志。</summary>
    public readonly record struct ClientAppCheck(bool Allowed, string Reason)
    {
        public static ClientAppCheck Ok() => new(true, string.Empty);

        public static ClientAppCheck Denied(string reason) => new(false, reason);
    }
}
