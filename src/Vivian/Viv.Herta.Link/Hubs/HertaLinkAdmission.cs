using Viv.Contracts.Models;
using Viv.Apex.Proxy.Ports;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 连接准入：身份齐不齐，以及这个 AppId 是不是 Apex 里启用的客户端。
    /// 抽出来是为了能脱离 Hub.Context 单测。
    /// </summary>
    [Viv.Contracts.Attributes.VivDependency(AsSelf = true)]
    public sealed class HertaLinkAdmission : Viv.Contracts.Interface.IDependency
    {
        private readonly IProxyApexAppService _apps;

        public HertaLinkAdmission(IProxyApexAppService apps)
        {
            _apps = apps;
        }

        /// <summary>不允许时返回的原因会写进日志，排障时能直接看出卡在哪一步。</summary>
        public async Task<AppCheckResult> CheckAsync(
            VivContextContent? identity,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
                return AppCheckResult.Denied("没有可信身份（网关头和 JWT 都没取到）");

            if (identity.AppId <= 0)
                return AppCheckResult.Denied("缺少 AppId");

            if (identity.SubjectId <= 0 || identity.UserId <= 0)
                return AppCheckResult.Denied($"身份不完整 subject={identity.SubjectId} user={identity.UserId}");

            // AppId 的有效性问 Apex，本地不维护白名单。
            // 这里只查「应用本身可用」：连接上下文的 JWT 里只有 AppId，没有用户类型，
            // 拿不到「该查哪张授权表」的信息。授权在登录时已经按库里的 UserType 校验过一次，
            // token 是 Herta 自签的，客户端改不了；准入再查一次只会多一次 RPC，不增加保证。
            return await _apps.CheckAppAsync(identity.AppId, cancellationToken).ConfigureAwait(false);
        }
    }
}
