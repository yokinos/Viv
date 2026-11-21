using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    /// <summary>
    /// <see cref="IUserPermissionChecker"/> 的 Apex 实现：用户 → 全部启用角色 → 掩码按位或 → 测目标位。
    ///
    /// <para><b>两套权限，两套位</b>：</para>
    /// <list type="bullet">
    /// <item><description>编码带冒号（<c>user:add</c>）= 操作权限，位下标 <c>AtMenuAction.BitIndex</c>，
    /// 对应 <c>AtUserRole.ButtonMask</c>；</description></item>
    /// <item><description>编码不带冒号（<c>user</c>）= 页面/模块权限，位下标 <c>AtMenu.BitIndex</c>，
    /// 对应 <c>AtUserRole.MenuMask</c>。给目录编一个 <c>user</c> 就是「有这个模块就能进」。</description></item>
    /// </list>
    ///
    /// <para><b>为什么是掩码而不是关联表</b>：授权单位是「角色的某几个页面 / 某几个动作」，
    /// 总量在几百的量级，两个 <c>BigInteger</c> 就够；判定是一次位与，不需要 join。</para>
    ///
    /// <para><b>超级管理员旁路</b>：<c>AtUser.IsSuperAdmin</c> 且账号可用时整体放行，连编码都不用解析。
    /// 这条旁路刻意写在业务实现里而不是 <c>[VivRequirePermission]</c> 里 ——
    /// 框架层不认识「谁是超管」这种业务分类，谁该被放过是应用自己的授权模型决定的。</para>
    ///
    /// <para><b>缓存</b>：编码→位号两张表、用户的授权快照（超管标记 + 掩码），都放 Redis（60 秒）。
    /// 不用进程内缓存 —— 多副本下「角色改了」各副本会不同步，而权限不同步的窗口是安全问题。
    /// 快照里带上超管标记，是为了让超管路径也只有一次 Redis 读，而不是额外查一次用户、或去信 token。</para>
    /// </summary>
    public class UserPermissionRepository : DataAccessBase, IUserPermissionChecker
    {
        /// <summary>缓存时长。角色/菜单/动作变更后最坏等这么久生效；有写路径时应主动调 Invalidate。</summary>
        private const int CacheSeconds = 60;
        private const string ActionBitMapKey = "Apex:ActionBitMap";
        private const string MenuBitMapKey = "Apex:MenuBitMap";
        private readonly IRedisService _redis;
        private readonly IUserRepository _users;

        public UserPermissionRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger, IRedisService redis, IUserRepository users)
            : base(context, dbContext, logger)
        {
            _redis = redis;
            _users = users;
        }

        public async Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
            {
                return false;
            }

            // 冒号是唯一的语法约定：有冒号 = 动作（操作权限），没有 = 菜单（页面/模块权限）
            var isAction = permissionCode.Contains(':');

            // 超管判定放在编码解析之前：刚上线的接口，编码可能还没入库，
            // 这时候不该把超管挡在门外。和登录侧「超管不要求绑定角色」是同一个口径。
            var grant = await LoadGrantAsync(userId, isAction).ConfigureAwait(false);

            if (grant.IsSuperAdmin)
            {
                return true;
            }

            var bitIndex = await ResolveBitIndexAsync(permissionCode, isAction).ConfigureAwait(false);

            // 编码查不到 = 拒绝。这通常是「特性里的编码和库里对不上」，属于配置错误，
            // 启动期校验会尽量提前报出来；这里绝不因为查不到就放行。
            if (bitIndex is null)
            {
                return false;
            }

            return BitIndexMaskMagic.Has(grant.Mask, bitIndex.Value);
        }

        /// <summary>页面权限掩码（位下标 = <c>AtMenu.BitIndex</c>）。给前端路由/菜单显隐用。</summary>
        public async Task<BigInteger> GetMenuMaskAsync(long userId)
        {
            if (userId <= 0)
            {
                return BigInteger.Zero;
            }

            var grant = await LoadGrantAsync(userId, isAction: false).ConfigureAwait(false);
            if (!grant.IsSuperAdmin)
            {
                return grant.Mask;
            }

            // 超管 = 所有启用菜单都可见。按位号表现拼，不要用 (1 << n) - 1 猜上界：
            // 位号是库里自增的，会漂移，猜出来的上界迟早对不上。
            var map = await LoadBitMapAsync(isAction: false).ConfigureAwait(false);
            return BitIndexMaskMagic.Combine(map.Values.Select(static x => BigInteger.One << x).ToArray());
        }

        /// <summary>
        /// 丢掉该用户的授权快照缓存。角色变更后必须调；改 <c>IsSuperAdmin</c>、禁用账号之后更要调 ——
        /// 否则超管旁路还会一路生效到缓存过期。
        /// </summary>
        public async Task InvalidateUserAsync(long userId)
        {
            if (userId <= 0)
            {
                return;
            }

            await _redis.RemoveAsync(GrantKey(userId, isAction: true)).ConfigureAwait(false);
            await _redis.RemoveAsync(GrantKey(userId, isAction: false)).ConfigureAwait(false);
        }

        /// <summary>丢掉编码→位号缓存。新增/改名/停用菜单或动作之后调。</summary>
        public async Task InvalidateBitMapAsync()
        {
            await _redis.RemoveAsync(ActionBitMapKey).ConfigureAwait(false);
            await _redis.RemoveAsync(MenuBitMapKey).ConfigureAwait(false);
        }

        /// <summary>
        /// 编码 → 位号。整张表缓存：菜单和动作合起来也就几百条，一次取全比按编码逐个查便宜。
        /// 停用的不进映射 —— 停用等同于收回权限。
        /// </summary>
        private async Task<int?> ResolveBitIndexAsync(string code, bool isAction)
        {
            var map = await LoadBitMapAsync(isAction).ConfigureAwait(false);
            return map.TryGetValue(code, out var bitIndex) ? bitIndex : null;
        }

        private async Task<Dictionary<string, int>> LoadBitMapAsync(bool isAction)
        {
            var key = isAction ? ActionBitMapKey : MenuBitMapKey;
            var map = await _redis.GetAsync<Dictionary<string, int>>(key).ConfigureAwait(false);

            if (map is null)
            {
                map = isAction
                    ? await BuildActionBitMapAsync().ConfigureAwait(false)
                    : await BuildMenuBitMapAsync().ConfigureAwait(false);

                await _redis.AddAsync(key, map, CacheSeconds).ConfigureAwait(false);
            }

            return map;
        }

        private async Task<Dictionary<string, int>> BuildActionBitMapAsync()
        {
            var actions = await _dbContext
                .FindListAsync<AtMenuAction>(x => x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return actions
                .Where(static x => !string.IsNullOrWhiteSpace(x.ActionCode))
                .GroupBy(static x => x.ActionCode!, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().BitIndex, StringComparer.Ordinal);
        }

        private async Task<Dictionary<string, int>> BuildMenuBitMapAsync()
        {
            var menus = await _dbContext
                .FindListAsync<AtMenu>(x => x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return menus
                .Where(static x => !string.IsNullOrWhiteSpace(x.MenuCode))
                .GroupBy(static x => x.MenuCode!, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().BitIndex, StringComparer.Ordinal);
        }

        /// <summary>
        /// 取该用户的授权快照，缓存优先。一次缓存读同时回答「是不是超管」和「有哪些位」两件事，
        /// 所以超管判定不会比原来的掩码判定更贵 —— 反而省掉了编码→位号那次读。
        /// </summary>
        private async Task<UserGrant> LoadGrantAsync(long userId, bool isAction)
        {
            var key = GrantKey(userId, isAction);
            var cached = await _redis.GetAsync<string>(key).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(cached))
            {
                return UserGrant.Parse(cached);
            }

            var user = await _users.GetUserAsync(userId).ConfigureAwait(false);

            // 被禁用（或已不存在）的账号不旁路：超管标记只在账号可用时才算数。
            // 顺手读到的这个状态，比把标记塞进 token 多一层兜底 —— 降权 / 禁用 60 秒内生效。
            var isSuperAdmin = user is { IsSuperAdmin: true, Status: EmStatus.Enabled };

            var grant = new UserGrant(
                isSuperAdmin,
                isSuperAdmin ? BigInteger.Zero : await CombineRoleMaskAsync(userId, isAction).ConfigureAwait(false));

            await _redis.AddAsync(key, grant.ToString(), CacheSeconds).ConfigureAwait(false);
            return grant;
        }

        /// <summary>
        /// 用户全部<b>启用</b>角色的某个掩码列按位或。没有角色 → 0（一位都没有）。
        /// 页面和操作共用这一套取数，只是取不同的列。
        /// </summary>
        private async Task<BigInteger> CombineRoleMaskAsync(long userId, bool isAction)
        {
            var relations = await _dbContext
                .FindListAsync<AtUserRoleRelation>(x => x.UserId == userId)
                .ConfigureAwait(false);

            if (relations.Count == 0)
            {
                return BigInteger.Zero;
            }

            var roleIds = relations.Select(static x => x.RoleId).Distinct().ToList();

            // 停用角色不参与：禁用就是禁用，不能靠缓存或旧 token 继续生效
            var roles = await _dbContext
                .FindListAsync<AtUserRole>(x => roleIds.Contains(x.Id) && x.Status == EmStatus.Enabled)
                .ConfigureAwait(false);

            return BitIndexMaskMagic.Combine(roles
                .Select(role => BitIndexMaskMagic.FromText(isAction ? role.ButtonMask : role.MenuMask))
                .ToArray());
        }

        /// <summary>
        /// 缓存 key 用了新前缀 <c>Apex:Grant:</c>，和老版本的 <c>Apex:RoleMask:</c>（裸掩码字符串）分开 ——
        /// 老值按新格式解析会抛异常，不该让灰度期间读到脏格式。
        /// </summary>
        private static string GrantKey(long userId, bool isAction)
            => $"Apex:Grant:{userId}:{(isAction ? "action" : "menu")}";

        /// <summary>
        /// 一个用户的授权快照：是不是超管 + 掩码。
        ///
        /// <para><b>缓存值用 <c>"0|12345"</c> / <c>"1|0"</c> 这种纯文本，不用 JSON</b>：
        /// 每个带权限特性的请求都要读它一次，走 <c>GetAsync&lt;string&gt;</c> 反序列化一个对象不值当；
        /// 而且这种格式在 redis-cli 里一眼能看懂。超管的掩码位存 0 —— 对他没有意义，真正起作用的只有标记。</para>
        /// </summary>
        private readonly record struct UserGrant(bool IsSuperAdmin, BigInteger Mask)
        {
            public static UserGrant Parse(string text)
            {
                var parts = text.Split('|', 2);

                // 格式非法直接抛，不吞成 0：吞掉会表现成「所有人忽然没权限」，
                // 比一条异常难查得多（和 BitIndexMaskMagic.FromText 同一个取舍）
                if (parts.Length != 2 || (parts[0] != "0" && parts[0] != "1"))
                {
                    throw new FormatException($"授权缓存格式非法：{text}");
                }

                return new UserGrant(parts[0] == "1", BitIndexMaskMagic.FromText(parts[1]));
            }

            public override string ToString()
                => $"{(IsSuperAdmin ? "1" : "0")}|{BitIndexMaskMagic.ToText(Mask)}";
        }
    }
}
