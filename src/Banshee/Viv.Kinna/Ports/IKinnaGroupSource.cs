using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 群成员来源。用户属于哪些群由业务回答，框架只在连接建立时拿来加组。
    ///
    /// 带 THub 是因为一个进程里两个 Hub 的成员来源通常是两套数据，
    /// 不带标记就只能注册一个实现。
    ///
    /// 不注册这个端口时连接照常建立，只是群消息投不到人。查询失败也不该让连接建立失败：
    /// 群服务抖一下把整条 IM 连接带下去，等于把可用性单点挂在群服务上。
    /// </summary>
    public interface IKinnaGroupSource<THub> where THub : Hub
    {
        Task<List<long>> GetUserGroupIdsAsync(long subjectId, long userId, CancellationToken cancellationToken = default);
    }
}
