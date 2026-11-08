using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 连接准入。框架只保证身份取得出来、字段齐不齐，这个 AppId 该不该放行由业务回答。
    ///
    /// 两套传输共用这一个端口：SignalR 的 Hub 和原生 WebSocket 端点走同一条准入，
    /// 否则同一个人换个客户端就能绕过限制。
    ///
    /// 带 THub 是因为一个进程里两个模块的准入数据通常不是一张表。
    /// </summary>
    public interface IKinnaAdmission<THub> where THub : Hub
    {
        /// <param name="identity">已验签的身份，取不到时为 null</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<KinnaAdmissionResult> CheckAsync(VivContextContent? identity, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 准入结论。<paramref name="Reason"/> 只进日志，不回给客户端——
    /// 把「缺少 AppId」这类内情告诉未通过的连接，等于帮人试探。
    /// </summary>
    public readonly record struct KinnaAdmissionResult(bool Allowed, string? Reason)
    {
        public static KinnaAdmissionResult Allow() => new(true, null);

        public static KinnaAdmissionResult Denied(string reason) => new(false, reason);
    }
}
