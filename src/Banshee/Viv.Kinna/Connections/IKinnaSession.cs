using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 一个 Hub 对应的一套运行时状态：连接表、发送器、Key 生成器、配置。
    ///
    /// 连接表和 Key 前缀不能跨 Hub 共用，所以按 Hub 各存一份。业务代码注入
    /// <c>IKinnaSession&lt;ChatHub&gt;</c> 拿到 ChatHub 那一套，注入别的 Hub 就是另一套。
    /// </summary>
    public interface IKinnaSession
    {
        /// <summary>模块名（Hub 类型名），用于日志和指标标签</summary>
        string Hub { get; }

        /// <summary>本进程连接表</summary>
        IKinnaConnectionPool Pool { get; }

        /// <summary>下行发送口，已绑定到本会话的 Hub 类型</summary>
        IKinnaClientSender Sender { get; }

        /// <summary>用户 Key 和群名的生成器</summary>
        IKinnaKeys Keys { get; }

        KinnaOptions Options { get; }
    }

    /// <summary>带 Hub 标记的会话，<typeparamref name="THub"/> 只用来区分模块。</summary>
    public interface IKinnaSession<THub> : IKinnaSession where THub : Hub
    {
    }

    internal sealed class KinnaSession<THub> : IKinnaSession<THub> where THub : Hub
    {
        public KinnaSession(
            IKinnaConnectionPool pool,
            IKinnaClientSender sender,
            IKinnaKeys keys,
            KinnaOptions options)
        {
            Pool = pool;
            Sender = sender;
            Keys = keys;
            Options = options;
        }

        public string Hub => typeof(THub).Name;

        public IKinnaConnectionPool Pool { get; }

        public IKinnaClientSender Sender { get; }

        public IKinnaKeys Keys { get; }

        public KinnaOptions Options { get; }
    }
}
