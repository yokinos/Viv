using MagicOnion;
using MagicOnion.Server;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Proxy.Contracts;
using Viv.Contracts.Interface;
using Viv.Entity.Enums;

namespace Viv.Apex.Api.Rpc
{
    /// <summary>
    /// 客户端应用查询（MagicOnion 服务）。Herta.Link 建立连接前会调它确认 AppId 有效。
    /// 验签与上下文水合在框架的 <c>VivIdentityFilter</c> 里完成，这里只做业务。
    /// 验签恢复出的 AppId 若大于 0，必须和请求体里的 AppId 相同，避免拿着 A 的签名去查 B。
    /// 没配 InternalToken 时上下文 AppId 是 0，仍按请求体查（本地开发）。
    ///
    /// 除了「应用存在且启用」，请求还可以带上「归属主体 + 用户类型」做<b>授权</b>判定：
    /// 同一个 AppId 被 A 租户买了，不代表 B 租户也能用。归属信息由调用方（Herta）从自己的
    /// 用户表里取，客户端无法伪造；<c>UserType &lt; 0</c> 表示调用方只想知道应用本身可不可用。
    /// </summary>
    public sealed class ApexAppQueryService : ServiceBase<IApexAppQueryService>, IApexAppQueryService
    {
        private readonly IClientAppRepository _apps;
        private readonly ITenantRepository _tenants;
        private readonly ICompanyRepository _companies;
        private readonly IOrgRepository _orgs;
        private readonly IVivContext _context;

        public ApexAppQueryService(IClientAppRepository apps, ITenantRepository tenants, ICompanyRepository companies, IOrgRepository orgs, IVivContext context)
        {
            _apps = apps;
            _tenants = tenants;
            _companies = companies;
            _orgs = orgs;
            _context = context;
        }

        public async UnaryResult<ValidateClientAppReply> ValidateClientAppAsync(ValidateClientAppRequest request)
        {
            if (_context.AppId > 0 && _context.AppId != request.AppId)
                return Deny(exists: false, enabled: false, "客户端与签名身份不一致");

            var app = await _apps.GetAppAsync(request.AppId).ConfigureAwait(false);

            if (app == null)
                return Deny(exists: false, enabled: false, "客户端不存在");

            if (app.Status != EmStatus.Enabled)
                return Deny(exists: true, enabled: false, "客户端已禁用", app.Name);

            // UserType < 0：连接准入路径，只确认应用本身可用（手上没有用户类型）。
            // Master：至高无上，只校验用户存在（那一步在 Herta 登录时做），这里不查任何授权表。
            if (request.UserType < 0 || request.UserType == (int)EmUserType.Master)
                return Allow(app.Name);

            if (request.SubjectId <= 0)
                return Deny(exists: true, enabled: true, "缺少归属主体", app.Name);

            var authorized = request.UserType switch
            {
                (int)EmUserType.OrgUser => await OrgAuthorizedAsync(request.SubjectId, request.AppId).ConfigureAwait(false),
                (int)EmUserType.CompanyUser => await CompanyAuthorizedAsync(request.SubjectId, request.AppId).ConfigureAwait(false),
                // C 端用户与租户员工走同一张租户授权表：差别在业务语义（发言权限），不在「买没买这个 App」
                (int)EmUserType.TenantUser or (int)EmUserType.EndUser
                    => await TenantAuthorizedAsync(request.SubjectId, request.AppId).ConfigureAwait(false),
                // 未知类型一律不放行：新增 EmUserType 时忘了在这里补分支，宁可登录失败也不要默认通过
                _ => false
            };

            return authorized
                ? Allow(app.Name)
                : Deny(exists: true, enabled: true, "该主体未开通此应用", app.Name);
        }

        /// <summary>
        /// 分页取客户端应用。定时任务（Coser 的 AppForeachBase）靠它遍历所有应用，
        /// 不把 AtClientApp 抄一份到别的服务。
        /// </summary>
        public async UnaryResult<GetClientAppPageReply> GetClientAppPageAsync(GetClientAppPageRequest request)
        {
            // 分页参数在这里收敛（客户端也可能不收敛），取消取自本次调用的 CallContext
            var page = await _apps.GetAppPageAsync(
                request.PageIndex,
                request.PageSize,
                keyword: null,
                Context.CallContext.CancellationToken).ConfigureAwait(false);

            var reply = new GetClientAppPageReply
            {
                TotalCount = (int)page.TotalCount,
                TotalPages = page.TotalPages
            };

            foreach (var app in page.Items)
            {
                reply.Items.Add(new ClientAppItem
                {
                    Id = app.Id,
                    AppKey = app.AppKey ?? string.Empty,
                    Name = app.Name ?? string.Empty,
                    Platform = (int)app.Platform,
                    ClientStack = (int)app.ClientStack,
                    Enabled = app.Status == EmStatus.Enabled
                });
            }

            return reply;
        }

        private async Task<bool> TenantAuthorizedAsync(long tenantId, long appId)
        {
            // 主体被禁用（或已删）时授权关系可能还留着，所以先看主体本身。
            // 这两次读的是同一个缓存 bucket，第二次不会回源。
            var tenant = await _tenants.GetTenantAsync(tenantId).ConfigureAwait(false);
            if (tenant == null || tenant.Status != EmStatus.Enabled)
                return false;

            var relations = await _tenants.GetTenantAppRelationsAsync(tenantId).ConfigureAwait(false);
            return relations.Any(x => x.ClientAppId == appId && x.Status == EmStatus.Enabled);
        }

        private async Task<bool> CompanyAuthorizedAsync(long companyId, long appId)
        {
            // 公司主体不存在时 GetCompanyAsync 返回 null（GetDbAsync 查不到实体就返回 null）
            var company = await _companies.GetCompanyAsync(companyId).ConfigureAwait(false);
            if (company == null || company.Status != EmStatus.Enabled)
                return false;

            var relations = await _companies.GetCompanyAppsAsync(companyId).ConfigureAwait(false);
            return relations.Any(x => x.ClientAppId == appId && x.Status == EmStatus.Enabled);
        }

        private async Task<bool> OrgAuthorizedAsync(long orgId, long appId)
        {
            var (org, relations) = await _orgs.GetOrgAsync(orgId).ConfigureAwait(false);
            if (org == null || org.Status != EmStatus.Enabled)
                return false;

            return relations?.Any(x => x.ClientAppId == appId && x.Status == EmStatus.Enabled) == true;
        }

        private static ValidateClientAppReply Allow(string? name) => new()
        {
            Exists = true,
            Enabled = true,
            Allowed = true,
            Name = name ?? string.Empty
        };

        private static ValidateClientAppReply Deny(bool exists, bool enabled, string message, string? name = null) => new()
        {
            Exists = exists,
            Enabled = enabled,
            Allowed = false,
            Name = name ?? string.Empty,
            Message = message
        };
    }
}
