namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 连接身份
    /// </summary>
    public readonly record struct ConnectionKey(long AppId, long SubjectId, long UserId);

    /// <summary>
    /// 连接快照
    /// </summary>
    public readonly record struct ConnectionInfo(string ConnectionId, long AppId, long SubjectId, long UserId);
}
