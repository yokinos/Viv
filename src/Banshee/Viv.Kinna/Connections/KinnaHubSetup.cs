using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Options;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 单个 Hub 的配置实例。
    ///
    /// <c>BuildOptions</c> 每次调用都会 Clone → Bind → 执行 configure 回调，而会话、连接表、
    /// 发送器是三个独立单例，各自调用一次就会把用户的 configure 执行三遍。这里只组装一次，
    /// 三处共用同一份，避免「回调里写了个计数器」这类副作用被放大。
    /// </summary>
    internal sealed class KinnaHubSetup<THub> where THub : Hub
    {
        public KinnaHubSetup(KinnaOptions options)
        {
            Options = options;
        }

        public KinnaOptions Options { get; }
    }
}
