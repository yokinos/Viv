using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 在线态。可选能力，不在投递路径上，投递只查本进程连接表；
    /// 这里给多端清点、好友在线这类查询用。
    ///
    /// 实现可以直接抛：框架会吞掉异常、记 <c>kinna.presence.failed{op}</c> 并打一条警告，
    /// 然后照常建立连接、照常回 Pong。在线态是展示信息，不该有「能不能连上」的裁决权 ——
    /// 以前这个约束只写在某个实现的注释里，换成会抛的实现就变成新连接全建不起来。
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
