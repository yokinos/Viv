using Viv.Herta.Core.IService;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Ports;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 把 Herta.Core 的群成员查询接到 Kinna 的端口上。也可以让 GroupService 直接实现端口，
    /// 但那样会把 SignalR 的框架引用带进 Core。
    /// </summary>
    public sealed class HertaGroupSource : IKinnaGroupSource<ChatHub>, Viv.Contracts.Interface.IDependency
    {
        private readonly IGroupService _groupService;

        public HertaGroupSource(IGroupService groupService)
        {
            _groupService = groupService;
        }

        public async Task<List<long>> GetUserGroupIdsAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
        {
            // GroupService 的仓储链路还没接 CancellationToken，这里至少在查库前响应一次取消：
            // 连接已经中止的话没必要再打一次数据库
            cancellationToken.ThrowIfCancellationRequested();

            return await _groupService.GetUserGroupIdsAsync(subjectId, userId).ConfigureAwait(false);
        }
    }
}
