using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 默认准入：只校验身份完整性，不查外部系统。
    ///
    /// 框架层拿不到业务的应用注册表，所以这是能给出的最强默认值。业务侧想校验
    /// 「这个 AppId 是否启用」时注册自己的 <see cref="IKinnaAdmission{THub}"/> 覆盖它。
    /// </summary>
    public sealed class KinnaIdentityAdmission<THub> : IKinnaAdmission<THub> where THub : Hub
    {
        public Task<KinnaAdmissionResult> CheckAsync(
            VivContextContent? identity,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
                return Task.FromResult(KinnaAdmissionResult.Denied("没有可信身份（网关头和 JWT 都没取到）"));

            if (identity.AppId <= 0)
                return Task.FromResult(KinnaAdmissionResult.Denied("缺少 AppId"));

            if (identity.SubjectId <= 0 || identity.UserId <= 0)
            {
                return Task.FromResult(KinnaAdmissionResult.Denied(
                    $"身份不完整 subject={identity.SubjectId} user={identity.UserId}"));
            }

            return Task.FromResult(KinnaAdmissionResult.Allow());
        }
    }
}
