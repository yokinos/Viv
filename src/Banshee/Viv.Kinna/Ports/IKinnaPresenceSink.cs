using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 在线态。可选能力，不在投递路径上，投递只查本进程连接表；
    /// 这里给多端清点、好友在线这类查询用。
    /// </summary>
    public interface IKinnaPresenceSink<THub> where THub : Hub
    {
        Task OnlineAsync(long subjectId, long userId);

        Task OfflineAsync(long subjectId, long userId);

        Task RenewAsync(long subjectId, long userId);
    }

    /// <summary>
    /// 没接在线态时的空实现。
    /// </summary>
    public sealed class NoopKinnaPresenceSink<THub> : IKinnaPresenceSink<THub> where THub : Hub
    {
        public static readonly NoopKinnaPresenceSink<THub> Instance = new();

        public Task OnlineAsync(long subjectId, long userId) => Task.CompletedTask;

        public Task OfflineAsync(long subjectId, long userId) => Task.CompletedTask;

        public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
    }
}
