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
        /// <summary>
        /// 登记连接。
        /// </summary>
        /// <returns>
        /// 这条连接的建立时间——必须是本表真正记下来的那个值。
        /// 广播顶号时要把同一个值发给别的副本，各自取一次时钟的话，
        /// 两边差几微秒就可能让两条连接都判不成「谁更旧」，最后留下两条。
        /// </returns>
        DateTimeOffset Add(string connectionId, long appId, long subjectId, long userId);

        /// <summary>
        /// 移除连接并返回它的身份，未知连接返回 null。
        /// 返回身份是为了让调用方直接拿去扣在线态，省掉一次 TryGet（两步之间会被清扫插进来）。
        /// </summary>
        KinnaConnectionInfo? Remove(string connectionId);

        /// <summary>
        /// 按用户查，不区分客户端。
        /// </summary>
        List<string> GetConnectionIds(long subjectId, long userId);

        /// <summary>
        /// 按用户加客户端查。
        /// </summary>
        List<string> GetConnectionIds(long appId, long subjectId, long userId);

        /// <summary>
        /// 按客户端查，**不分租户也不分用户**：拿到这个 AppId 在本副本上的全部连接。
        ///
        /// 给「某一端整体强制重登」用（版本有 bug、客户端被下架）。这种方法靠遍历
        /// <c>_connectionLookup</c> 而不是专门维护一本索引：踢人是低频管理动作，
        /// 而连接建立/断开是热路径，为一本可能一年用不上几次的索引给它加维护成本不值。
        /// </summary>
        List<string> GetConnectionIdsByApp(long appId);

        List<KinnaConnectionInfo> GetConnections(long subjectId);

        /// <summary>
        /// 查同一客户端上**建立时间早于**给定连接的连接（顶号用）。
        ///
        /// 比的是 <c>(建立时间, 连接 Id)</c> 这个全序，不是单纯比时间：
        /// 两条连接几乎同时到达两个副本时时间可能相同，只比时间会两边都不踢（留下两条）
        /// 或者两边都踢（互相踢掉、来回抖动）；带上连接 Id 就恰好只有一条被判为更旧。
        /// <paramref name="connectionId"/> 自己永远不算。
        /// </summary>
        List<KinnaConnectionInfo> GetOlderConnections(
            long appId,
            long subjectId,
            long userId,
            DateTimeOffset establishedAt,
            string connectionId);

        /// <summary>
        /// 不在本进程时返回 null。
        /// </summary>
        KinnaConnectionInfo? TryGet(string connectionId);

        /// <summary>
        /// 续租，客户端 Ping 时调用。
        /// </summary>
        void Touch(string connectionId);

        /// <summary>
        /// 清掉超过 ttl 没续租的连接，返回被清掉的那些连接。
        /// </summary>
        List<KinnaConnectionInfo> SweepExpired(TimeSpan ttl);

        /// <summary>
        /// 给健康检查看的一眼快照。
        /// </summary>
        KinnaConnectionPoolSnapshot GetSnapshot();

        /// <summary>
        /// 测试和停机用。
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 连接表现状。<paramref name="MaxIdle"/> 是最久没续租的那条连接已经闲置多久，
    /// 空表时为 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    public readonly record struct KinnaConnectionPoolSnapshot(int Connections, TimeSpan MaxIdle);
}
