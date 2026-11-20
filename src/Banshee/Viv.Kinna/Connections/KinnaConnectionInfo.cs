namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 连接身份
    /// </summary>
    public readonly record struct KinnaConnectionKey(long AppId, long SubjectId, long UserId);

    /// <summary>
    /// 连接快照。
    ///
    /// <paramref name="ConnectedAt"/> 是**建立时间**，和心跳时间（最后续租）不是一回事：
    /// 跨副本顶号要靠它判断「谁更旧」，用心跳时间的话两个副本都在续租，谁新谁旧看不出来。
    ///
    /// <paramref name="LastSeen"/> 是最后一次续租的时间，租约到期就是 <c>LastSeen + ttl</c>。
    /// 心跳被限流时要把这个真实值回给客户端——回 <c>now + ttl</c> 等于告诉它「刚续过」，
    /// 而表里的倒计时一秒都没停。为 null 表示心跳表里没有这条（连接刚摘掉）。
    /// </summary>
    public readonly record struct KinnaConnectionInfo(
        string ConnectionId,
        long AppId,
        long SubjectId,
        long UserId,
        DateTimeOffset ConnectedAt = default,
        DateTimeOffset? LastSeen = null);
}
