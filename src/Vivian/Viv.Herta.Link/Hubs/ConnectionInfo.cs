namespace Viv.Herta.Link.Hubs
{
    public readonly record struct ConnectionKey(long TenantId, long UserId, long AppId);

    public readonly record struct ConnectionInfo(string ConnectionId, long TenantId, long UserId, long AppId);
}
