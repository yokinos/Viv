namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 本进程连接表，实例广播拓扑下的唯一投递依据：每个副本都收到全量消息，
    /// 「本表里没有」就是「这个人不在这台机器上」。
    ///
    /// 全部方法都是本地语义，集群范围的操作走 MQ 广播，不在这里。
    /// 参数顺序统一 AppId, SubjectId, UserId（与 ConnectionKey / IVivContext 一致）；三个都是 long，写反没有编译期提示。
    /// </summary>
    public interface IConnectionPool
    {
        void Add(string connectionId, long appId, long subjectId, long userId);

        /// <summary>未登记过的连接是空操作</summary>
        void Remove(string connectionId);

        /// <summary>按用户，不区分客户端</summary>
        List<string> GetConnectionIds(long subjectId, long userId);

        List<string> GetConnectionIds(long appId, long subjectId, long userId);

        List<ConnectionInfo> GetConnections(long subjectId);

        /// <summary>按连接Id 取身份；不在本进程返回 null</summary>
        ConnectionInfo? TryGet(string connectionId);

        /// <summary>续租（客户端 Ping 时调用）</summary>
        void Touch(string connectionId);

        /// <summary>剔除超过 ttl 没续租的连接，返回被剔掉的 connectionId</summary>
        List<string> SweepExpired(TimeSpan ttl);

        Task ForceDisconnectAsync(string connectionId, CancellationToken cancellationToken = default);

        Task ForceDisconnectUserAsync(long subjectId, long userId, CancellationToken cancellationToken = default);

        /// <summary>只覆盖本副本；全租户下线要走 MQ 广播</summary>
        Task ForceDisconnectSubjectAsync(long subjectId, CancellationToken cancellationToken = default);

        /// <summary>测试与停机用</summary>
        void Clear();
    }
}
