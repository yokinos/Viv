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
    /// </summary>
    public readonly record struct KinnaConnectionInfo(
        string ConnectionId,
        long AppId,
        long SubjectId,
        long UserId,
        DateTimeOffset ConnectedAt = default);
}
