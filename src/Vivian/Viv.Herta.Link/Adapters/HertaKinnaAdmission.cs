using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Ports;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 准入端口到 <see cref="HertaLinkAdmission"/> 的桥。
    ///
    /// 存在的意义是让「未启用的 AppId 不能连」这条规则只有一份实现：Hub 和原生 WebSocket
    /// 端点都问到这里，换传输不会换判定。
    /// </summary>
    public sealed class HertaKinnaAdmission<THub> : IKinnaAdmission<THub> where THub : Hub
    {
        private readonly HertaLinkAdmission _admission;

        public HertaKinnaAdmission(HertaLinkAdmission admission)
        {
            _admission = admission;
        }

        public async Task<KinnaAdmissionResult> CheckAsync(
            VivContextContent? identity,
            CancellationToken cancellationToken = default)
        {
            var result = await _admission.CheckAsync(identity, cancellationToken).ConfigureAwait(false);

            return result.Allowed
                ? KinnaAdmissionResult.Allow()
                : KinnaAdmissionResult.Denied(result.Reason);
        }
    }
}
