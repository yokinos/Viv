namespace Viv.Kinna.Connections
{
    /// <summary>连接身份。顺序跟 IVivContext 一致：AppId、SubjectId、UserId。</summary>
    public readonly record struct KinnaConnectionKey(long AppId, long SubjectId, long UserId);

    /// <summary>连接快照</summary>
    public readonly record struct KinnaConnectionInfo(string ConnectionId, long AppId, long SubjectId, long UserId);
}
