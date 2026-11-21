using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Elysia.Interface;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    /// <summary>
    /// <see cref="IUserPermissionChecker"/> 的 Apex 实现：
    /// 用户 → 全部启用角色 → <b>本应用</b>的授权行 → 掩码按位或 → 测目标位。
    ///
    /// <para><b>两套权限，两套位</b>（位号都是<b>应用内</b>自增）：</para>
    /// <list type="bullet">
    /// <item><description>编码带冒号（<c>user:add</c>）= 操作权限，位下标 <c>AtMenuAction.BitIndex</c>，
    /// 对应 <c>AtUserRoleApp.ButtonMask</c>；</description></item>
    /// <item><description>编码不带冒号（<c>user</c>）= 页面/模块权限，位下标 <c>AtMenu.BitIndex</c>，
    /// 对应 <c>AtUserRoleApp.MenuMask</c>。给目录编一个 <c>user</c> 就是「有这个模块就能进」。</description></item>
    /// </list>
    ///
    /// <para><b>为什么按应用取</b>：一个角色跨多个应用（同一岗位在不同端权限不同），而位号是应用内
    /// 分配的 —— 掩码必须带应用维度，否则 A 应用的第 3 位和 B 应用的第 3 位会互相授权。
    /// 应用来自 <see cref="IVivContext.AppId"/>（token 里的客户端应用），全仓的应用隔离都靠它；
    /// <c>AppId == 0</c>（服务间调用、没有登录身份）就没有任何授权可言，一律拒绝。</para>
    ///
    /// <para><b>超级管理员旁路</b>：<c>AtUser.IsSuperAdmin</c> 且账号可用时整体放行，连编码都不用解析。
    /// 这条旁路刻意写在业务实现里而不是 <c>[VivRequirePermission]</c> 里 ——
    /// 框架层不认识「谁是超管」这种业务分类，谁该被放过是应用自己的授权模型决定的。</para>
    ///
    /// <para><b>缓存</b>：都用 Redis Hash，field 是 AppId ——
    /// <c>Apex:Grant:{userId}</c>（授权快照）、<c>Apex:BitMap:menu</c> / <c>Apex:BitMap:action</c>（编码→位号）。
    /// 用 Hash 而不是「按应用拼 key」的唯一理由是失效：改一个用户的权限要一次清掉他<b>所有</b>应用、
    /// 改一个应用的菜单要一次清掉<b>所有</b>应用映射进来的那张表，按 key 拼就得先枚举 —— 漏一个就是
    /// 权限不同步，而权限不同步的窗口是安全问题。Hash 一个 DEL 全清。</para>
    /// </summary>
    public class UserPermissionRepository : DataAccessBase, IUserPermissionChecker
    {
        /// <summary>
        /// 缓存时长。角色/菜单/动作变更后最坏等这么久生效；有写路径时应主动调 Invalidate。
        ///
        /// <para>Hash 的 key 级 TTL 会随每次写入被推后（同一用户的不同应用先后写入），所以快照值里
        /// 另存写入时间戳，判定时按它算新鲜度 —— 否则最早写入的那个应用能活到两倍时长。</para>
        /// </summary>
        private const int CacheSeconds = 60;

        private const string MenuBitMapKey = "Apex:BitMap:menu";
        private const string ActionBitMapKey = "Apex:BitMap:action";

        private static readonly Dictionary<string, int> EmptyBitMap = [];

        private readonly IRedisService _redis;

        private readonly IUserRepository _users;

        private readonly TimeProvider _time;

        public UserPermissionRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger,
            IRedisService redis, IUserRepository users, TimeProvider? timeProvider = null)
            : base(context, dbContext, logger)
        {
            _redis = redis;
            _users = users;
            _time = timeProvider ?? TimeProvider.System;
        }

        public async Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
            {
                return false;
            }

            // 冒号是唯一的语法约定：有冒号 = 动作（操作权限），没有 = 菜单（页面/模块权限）
            var isAction = permissionCode.Contains(':');

            var appId = _vivContext.AppId;
            var grant = await LoadGrantAsync(userId, appId).ConfigureAwait(false);

            // 超管判定放在编码解析之前：刚上线的接口，编码可能还没入库，
            // 这时候不该把超管挡在门外。和登录侧「超管不要求绑定角色」是同一个口径。
            if (grant.IsSuperAdmin)
            {
                return true;
            }

            var bitIndex = await ResolveBitIndexAsync(permissionCode, isAction, appId).ConfigureAwait(false);

            // 编码查不到 = 拒绝。这通常是「特性里的编码和库里对不上」，属于配置错误，
            // 启动期校验会尽量提前报出来；这里绝不因为查不到就放行。
            if (bitIndex is null)
            {
                return false;
            }

            return BitIndexMaskMagic.Has(isAction ? grant.ButtonMask : grant.MenuMask, bitIndex.Value);
        }

        /// <summary>页面权限掩码（位下标 = 本应用的 <c>AtMenu.BitIndex</c>）。给前端路由/菜单显隐用。</summary>
        public async Task<BigInteger> GetMenuMaskAsync(long userId)
        {
            if (userId <= 0)
            {
                return BigInteger.Zero;
            }

            var appId = _vivContext.AppId;
            var grant = await LoadGrantAsync(userId, appId).ConfigureAwait(false);
            if (!grant.IsSuperAdmin)
            {
                return grant.MenuMask;
            }

            // 超管 = 本应用所有启用菜单都可见。按位号表现拼，不要用 (1 << n) - 1 猜上界：
            // 位号是库里自增的，会漂移，猜出来的上界迟早对不上。
            var map = await LoadBitMapAsync(isAction: false, appId).ConfigureAwait(false);
            return BitIndexMaskMagic.Combine(map.Values.Select(static x => BigInteger.One << x).ToArray());
        }

        /// <summary>
        /// 丢掉该用户的授权快照缓存（<b>所有应用</b>）。角色授权变更后必须调；
        /// 改 <c>IsSuperAdmin</c>、禁用账号、改用户角色之后更要调 ——
        /// 否则超管旁路还会一路生效到缓存过期。
        /// </summary>
        public async Task InvalidateUserAsync(long userId)
        {
            if (userId <= 0)
            {
                return;
            }

            await _redis.RemoveAsync(GrantKey(userId)).ConfigureAwait(false);
        }

        /// <summary>丢掉编码→位号缓存（<b>所有应用</b>）。新增/改名/停用菜单或动作之后调。</summary>
        public async Task InvalidateBitMapAsync()
        {
            await _redis.RemoveAsync(MenuBitMapKey).ConfigureAwait(false);
            await _redis.RemoveAsync(ActionBitMapKey).ConfigureAwait(false);
        }

        /// <summary>
        /// 编码 → 位号。整张表按应用缓存：一个应用的菜单和动作合起来也就几十条，
        /// 一次取全比按编码逐个查便宜。停用的不进映射 —— 停用等同于收回权限。
        /// </summary>
        private async Task<int?> ResolveBitIndexAsync(string code, bool isAction, long appId)
        {
            var map = await LoadBitMapAsync(isAction, appId).ConfigureAwait(false);
            return map.TryGetValue(code, out var bitIndex) ? bitIndex : null;
        }

        private async Task<Dictionary<string, int>> LoadBitMapAsync(bool isAction, long appId)
        {
            if (appId <= 0)
            {
                return EmptyBitMap;
            }

            var key = isAction ? ActionBitMapKey : MenuBitMapKey;
            var field = AppField(appId);

            var map = await _redis.HashGetAsync<Dictionary<string, int>>(key, field).ConfigureAwait(false);
            if (map is not null)
            {
                return map;
            }

            map = isAction
                ? await BuildActionBitMapAsync(appId).ConfigureAwait(false)
                : await BuildMenuBitMapAsync(appId).ConfigureAwait(false);

            await _redis.HashSetAsync(key, field, map).ConfigureAwait(false);
            await _redis.SetKeyExpireAsync(key, TimeSpan.FromSeconds(CacheSeconds)).ConfigureAwait(false);
            return map;
        }

        private async Task<Dictionary<string, int>> BuildActionBitMapAsync(long appId)
        {
            var menuIds = await GetAppMenuIdsAsync(appId).ConfigureAwait(false);
            if (menuIds.Count == 0)
            {
                return EmptyBitMap;
            }

            var actions = await _dbContext
                .FindListAsync<AtMenuAction>(x => menuIds.Contains(x.MenuId) && x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return actions
                .Where(static x => !string.IsNullOrWhiteSpace(x.ActionCode))
                .GroupBy(static x => x.ActionCode!, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().BitIndex, StringComparer.Ordinal);
        }

        private async Task<Dictionary<string, int>> BuildMenuBitMapAsync(long appId)
        {
            var menus = await _dbContext
                .FindListAsync<AtMenu>(x => x.ClientAppId == appId && x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return menus
                .Where(static x => !string.IsNullOrWhiteSpace(x.MenuCode))
                .GroupBy(static x => x.MenuCode!, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().BitIndex, StringComparer.Ordinal);
        }

        private async Task<List<long>> GetAppMenuIdsAsync(long appId)
        {
            var menus = await _dbContext
                .FindListAsync<AtMenu>(x => x.ClientAppId == appId)
                .ConfigureAwait(false);

            return menus.Select(static x => x.Id).ToList();
        }

        /// <summary>
        /// 取该用户在某个应用下的授权快照，缓存优先。一次缓存读同时回答「是不是超管」和「两套位」，
        /// 所以超管判定不会比原来的掩码判定更贵 —— 反而省掉了编码→位号那次读。
        /// </summary>
        private async Task<UserGrant> LoadGrantAsync(long userId, long appId)
        {
            // 没有应用维度就没有授权可言（超管除外，他的标记跟应用无关）
            if (appId <= 0)
            {
                return await BuildGrantAsync(userId, appId).ConfigureAwait(false);
            }

            var key = GrantKey(userId);
            var field = AppField(appId);

            var cached = await _redis.HashGetAsync<string>(key, field).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cached))
            {
                var grant = UserGrant.Parse(cached);

                // key 级 TTL 会被同用户其它应用的写入推后，所以新鲜度按值里的时间戳算
                if (!grant.IsExpired(_time.GetUtcNow(), CacheSeconds))
                {
                    return grant;
                }
            }

            var rebuilt = await BuildGrantAsync(userId, appId).ConfigureAwait(false);

            await _redis.HashSetAsync(key, field, rebuilt.ToString()).ConfigureAwait(false);
            await _redis.SetKeyExpireAsync(key, TimeSpan.FromSeconds(CacheSeconds)).ConfigureAwait(false);
            return rebuilt;
        }

        private async Task<UserGrant> BuildGrantAsync(long userId, long appId)
        {
            var user = await _users.GetUserAsync(userId).ConfigureAwait(false);

            // 被禁用（或已不存在）的账号不旁路：超管标记只在账号可用时才算数。
            // 顺手读到的这个状态，比把标记塞进 token 多一层兜底 —— 降权 / 禁用 60 秒内生效。
            var isSuperAdmin = user is { IsSuperAdmin: true, Status: EmStatus.Enabled };

            var (menuMask, buttonMask) = isSuperAdmin || appId <= 0
                ? (BigInteger.Zero, BigInteger.Zero)
                : await CombineRoleMasksAsync(userId, appId).ConfigureAwait(false);

            return new UserGrant(isSuperAdmin, menuMask, buttonMask, _time.GetUtcNow());
        }

        /// <summary>
        /// 用户全部<b>启用</b>角色在<b>该应用下</b>的授权行，两套掩码各按位或。
        /// 没有角色 / 角色没分配到该应用 / 授权行被禁用 → 0（一位都没有）。
        /// </summary>
        private async Task<(BigInteger MenuMask, BigInteger ButtonMask)> CombineRoleMasksAsync(long userId, long appId)
        {
            var relations = await _dbContext
                .FindListAsync<AtUserRoleRelation>(x => x.UserId == userId)
                .ConfigureAwait(false);

            if (relations.Count == 0)
            {
                return (BigInteger.Zero, BigInteger.Zero);
            }

            var roleIds = relations.Select(static x => x.RoleId).Distinct().ToList();

            // 停用角色不参与：禁用就是禁用，不能靠缓存或旧 token 继续生效
            var roles = await _dbContext
                .FindListAsync<AtUserRole>(x => roleIds.Contains(x.Id) && x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            if (roles.Count == 0)
            {
                return (BigInteger.Zero, BigInteger.Zero);
            }

            var activeRoleIds = roles.Select(static x => x.Id).ToList();

            var grants = await _dbContext
                .FindListAsync<AtUserRoleApp>(x => activeRoleIds.Contains(x.RoleId)
                                                    && x.ClientAppId == appId
                                                    && x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return (
                BitIndexMaskMagic.Combine(grants.Select(static x => BitIndexMaskMagic.FromText(x.MenuMask)).ToArray()),
                BitIndexMaskMagic.Combine(grants.Select(static x => BitIndexMaskMagic.FromText(x.ButtonMask)).ToArray()));
        }

        /// <summary>
        /// 缓存 key 用了新前缀 <c>Apex:Grant:</c>，和老版本按「用户+类型」拼的
        /// <c>Apex:RoleMask:</c>（裸掩码字符串）分开 —— 老值按新格式解析会抛异常。
        /// </summary>
        private static string GrantKey(long userId) => $"Apex:Grant:{userId}";

        private static string AppField(long appId) => appId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 一个用户在一个应用下的授权快照：是不是超管 + 两套掩码 + 写入时间。
        ///
        /// <para><b>缓存值用 <c>"1730000000|0|12345|678"</c> 这种纯文本，不用 JSON</b>：
        /// 每个带权限特性的请求都要读它一次，走 <c>HashGetAsync&lt;string&gt;</c> 反序列化一个对象不值当；
        /// 而且这种格式在 redis-cli 里一眼能看懂。超管的掩码位存 0 —— 对他没有意义。</para>
        /// </summary>
        private readonly record struct UserGrant(bool IsSuperAdmin, BigInteger MenuMask, BigInteger ButtonMask, DateTimeOffset WrittenAt)
        {
            public bool IsExpired(DateTimeOffset now, int seconds)
                => now - WrittenAt >= TimeSpan.FromSeconds(seconds);

            public static UserGrant Parse(string text)
            {
                var parts = text.Split('|');

                // 格式非法直接抛，不吞成 0：吞掉会表现成「所有人忽然没权限」，
                // 比一条异常难查得多（和 BitIndexMaskMagic.FromText 同一个取舍）
                if (parts.Length != 4
                    || (parts[1] is not ("0" or "1"))
                    || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    throw new FormatException($"授权缓存格式非法：{text}");
                }

                return new UserGrant(
                    parts[1] == "1",
                    BitIndexMaskMagic.FromText(parts[2]),
                    BitIndexMaskMagic.FromText(parts[3]),
                    DateTimeOffset.FromUnixTimeSeconds(seconds));
            }

            public override string ToString()
                => string.Join('|',
                    WrittenAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    IsSuperAdmin ? "1" : "0",
                    BitIndexMaskMagic.ToText(MenuMask),
                    BitIndexMaskMagic.ToText(ButtonMask));
        }
    }
}
