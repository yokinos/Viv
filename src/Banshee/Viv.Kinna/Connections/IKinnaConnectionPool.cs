namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 本进程连接表。每台副本都会收到全量消息，所以查不到某个人就意味着他不在这台机器上，
    /// 不用去问其他副本或者 Redis。
    ///
    /// 只做本地读写。踢人那种既要改状态又要发消息的操作放在协调器里，连接表本身不碰 SignalR。
    /// 参数顺序统一 AppId、SubjectId、UserId，跟 IVivContext 一致。
    /// </summary>
    public interface IKinnaConnectionPool
    {
        void Add(string connectionId, long appId, long subjectId, long userId);

        /// <summary>
        /// 移除连接并返回它的身份，未知连接返回 null。
        /// 返回身份是为了让调用方直接拿去扣在线态，省掉一次 TryGet（两步之间会被清扫插进来）。
        /// </summary>
        KinnaConnectionInfo? Remove(string connectionId);

        /// <summary>按用户查，不区分客户端。</summary>
        List<string> GetConnectionIds(long subjectId, long userId);

        /// <summary>按用户加客户端查。</summary>
        List<string> GetConnectionIds(long appId, long subjectId, long userId);

        List<KinnaConnectionInfo> GetConnections(long subjectId);

        /// <summary>不在本进程时返回 null。</summary>
        KinnaConnectionInfo? TryGet(string connectionId);

        /// <summary>续租，客户端 Ping 时调用。</summary>
        void Touch(string connectionId);

        /// <summary>清掉超过 ttl 没续租的连接，返回被清掉的那些连接。</summary>
        List<KinnaConnectionInfo> SweepExpired(TimeSpan ttl);

        /// <summary>给健康检查看的一眼快照。</summary>
        KinnaConnectionPoolSnapshot GetSnapshot();

        /// <summary>测试和停机用。</summary>
        void Clear();
    }

    /// <summary>
    /// 连接表现状。<paramref name="MaxIdle"/> 是最久没续租的那条连接已经闲置多久，
    /// 空表时为 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    public readonly record struct KinnaConnectionPoolSnapshot(int Connections, TimeSpan MaxIdle);
}
