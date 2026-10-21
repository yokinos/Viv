using Viv.Contracts.Models;
using Viv.ServiceProxy.Apex;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 连接准入：身份齐不齐，以及这个 AppId 是不是 Apex 里启用的客户端。
    /// 抽出来是为了能脱离 Hub.Context 单测。
    /// </summary>
    public sealed class HertaLinkAdmission
    {
        private readonly IClientAppQuery _apps;

        public HertaLinkAdmission(IClientAppQuery apps)
        {
            _apps = apps;
        }

        /// <summary>不允许时返回的原因会写进日志，排障时能直接看出卡在哪一步。</summary>
        public async Task<ClientAppCheck> CheckAsync(
            VivContextContent? identity,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
                return ClientAppCheck.Denied("没有可信身份（网关头和 JWT 都没取到）");

            if (identity.AppId <= 0)
                return ClientAppCheck.Denied("缺少 AppId");

            if (identity.SubjectId <= 0 || identity.UserId <= 0)
                return ClientAppCheck.Denied($"身份不完整 subject={identity.SubjectId} user={identity.UserId}");

            // AppId 的有效性问 Apex，本地不维护白名单
            return await _apps.ValidateAsync(identity.AppId, cancellationToken).ConfigureAwait(false);
        }
    }
}
