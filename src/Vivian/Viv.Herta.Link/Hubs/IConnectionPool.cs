namespace Viv.Herta.Link.Hubs
{
    public interface IConnectionPool
    {
        void Add(string connectionId, long tenantId, long userId, long appId);

        void Remove(string connectionId);

        List<string> GetConnectionIds(long tenantId, long userId);

        List<string> GetConnectionIds(long tenantId, long userId, long appId);

        List<ConnectionInfo> GetConnections(long tenantId);

        Task ForceDisconnectAsync(string connectionId, CancellationToken cancellationToken = default);

        Task ForceDisconnectUserAsync(long tenantId, long userId, CancellationToken cancellationToken = default);

        Task ForceDisconnectTenantAsync(long tenantId, CancellationToken cancellationToken = default);

        void Clear();
    }
}
