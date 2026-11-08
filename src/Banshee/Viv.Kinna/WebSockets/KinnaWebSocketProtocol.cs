using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 上行帧到协议应答的翻译。抽出来是为了脱开 Socket 单测：
    /// 这里只依赖协调器，不碰传输。
    ///
    /// 上行的三种指令和 SignalR 的三个 Hub 方法一一对应，语义完全一样：
    /// <c>ping</c> ↔ <c>Ping()</c>、<c>ack</c> ↔ <c>Ack(long[])</c>、<c>resync</c> ↔ <c>Resync(long?)</c>。
    /// </summary>
    internal static class KinnaWebSocketProtocol
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// 解析一条上行帧。格式不对返回 null——调用方忽略即可，不必断连接：
        /// 客户端可能只是发了个别的指令，服务端不认识不代表它是恶意的。
        /// </summary>
        internal static KinnaClientMessage? Parse(byte[] utf8)
        {
            if (utf8.Length == 0)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<KinnaClientMessage>(utf8, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 按指令调协调器，返回要回给客户端的那一帧。不认识的指令返回 <see cref="KinnaOutbound.None"/>。
        /// </summary>
        internal static async Task<KinnaOutbound> DispatchAsync<THub>(
            KinnaClientMessage message,
            IKinnaConnectionCoordinator<THub> coordinator,
            string connectionId,
            CancellationToken cancellationToken = default) where THub : Hub
        {
            switch (message.Type)
            {
                case KinnaInboundMethods.Ping:
                {
                    var pong = await coordinator.PingAsync(connectionId, cancellationToken).ConfigureAwait(false);

                    // 服务端已经不认这条连接了：Pong 还是要回（客户端才知道发生了什么），
                    // 但回完就得关掉，否则它会继续用一条什么消息都收不到的连接
                    return new KinnaOutbound(KinnaClientMethods.Pong, pong, Close: pong.Alive == false);
                }

                case KinnaInboundMethods.Ack:
                    return new KinnaOutbound(
                        KinnaClientMethods.Ack,
                        await coordinator
                            .AckAsync(connectionId, message.Ids ?? [], cancellationToken)
                            .ConfigureAwait(false));

                case KinnaInboundMethods.Resync:
                    return new KinnaOutbound(
                        KinnaClientMethods.Resync,
                        await coordinator
                            .ResyncAsync(connectionId, message.LastMessageId, cancellationToken)
                            .ConfigureAwait(false));

                default:
                    return KinnaOutbound.None;
            }
        }
    }

    /// <summary>
    /// 一条上行指令对应的下行帧。<c>Method</c> 为 null 表示不需要回；
    /// <c>Close</c> 为 true 表示回完这一帧就断开（服务端已不再持有这条连接）。
    /// </summary>
    internal readonly record struct KinnaOutbound(string? Method, object? Payload, bool Close = false)
    {
        internal static KinnaOutbound None => new(null, null);
    }
}
