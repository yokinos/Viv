using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
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
    /// <para><b>缓存</b>：编码→位号两张表、用户的两个合并掩码，都放 Redis（60 秒）。
    /// 不用进程内缓存 —— 多副本下「角色改了」各副本会不同步，而权限不同步的窗口是安全问题。</para>
    /// </summary>
    public class UserPermissionRepository : DataAccessBase, IUserPermissionChecker
    {
        /// <summary>缓存时长。角色/菜单/动作变更后最坏等这么久生效；有写路径时应主动调 Invalidate。</summary>
        private const int CacheSeconds = 60;

        private const string ActionBitMapKey = "Apex:ActionBitMap";
        private const string MenuBitMapKey = "Apex:MenuBitMap";

        private readonly IRedisService _redis;

        public UserPermissionRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger, IRedisService redis)
            : base(context, dbContext, logger)
        {
            _redis = redis;
        }

        public async Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
            {
                return false;
            }

            // 冒号是唯一的语法约定：有冒号 = 动作（操作权限），没有 = 菜单（页面/模块权限）
            var isAction = permissionCode.Contains(':');

            var bitIndex = await ResolveBitIndexAsync(permissionCode, isAction).ConfigureAwait(false);

            // 编码查不到 = 拒绝。这通常是「特性里的编码和库里对不上」，属于配置错误，
            // 启动期校验会尽量提前报出来；这里绝不因为查不到就放行。
            if (bitIndex is null)
            {
                return false;
            }

            var mask = await LoadMaskAsync(userId, isAction).ConfigureAwait(false);
            return BitIndexMaskMagic.Has(mask, bitIndex.Value);
        }

        /// <summary>页面权限掩码（位下标 = <c>AtMenu.BitIndex</c>）。给前端路由/菜单显隐用。</summary>
        public Task<BigInteger> GetMenuMaskAsync(long userId)
            => userId <= 0 ? Task.FromResult(BigInteger.Zero) : LoadMaskAsync(userId, isAction: false);

        /// <summary>丢掉该用户的两种掩码缓存。角色、菜单、动作变更后必须调（漏调最多等 60 秒）。</summary>
        public async Task InvalidateUserAsync(long userId)
        {
            if (userId <= 0)
            {
                return;
            }

            await _redis.RemoveAsync(MaskKey(userId, isAction: true)).ConfigureAwait(false);
            await _redis.RemoveAsync(MaskKey(userId, isAction: false)).ConfigureAwait(false);
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
            var key = isAction ? ActionBitMapKey : MenuBitMapKey;
            var map = await _redis.GetAsync<Dictionary<string, int>>(key).ConfigureAwait(false);

            if (map is null)
            {
                map = isAction
                    ? await BuildActionBitMapAsync().ConfigureAwait(false)
                    : await BuildMenuBitMapAsync().ConfigureAwait(false);

                await _redis.AddAsync(key, map, CacheSeconds).ConfigureAwait(false);
            }

            return map.TryGetValue(code, out var bitIndex) ? bitIndex : null;
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

        private async Task<BigInteger> LoadMaskAsync(long userId, bool isAction)
        {
            var key = MaskKey(userId, isAction);
            var cached = await _redis.GetAsync<string>(key).ConfigureAwait(false);

            if (cached is not null)
            {
                return BitIndexMaskMagic.FromText(cached);
            }

            var mask = await CombineRoleMaskAsync(userId, isAction).ConfigureAwait(false);

            await _redis.AddAsync(key, BitIndexMaskMagic.ToText(mask), CacheSeconds).ConfigureAwait(false);
            return mask;
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

        private static string MaskKey(long userId, bool isAction)
            => $"Apex:RoleMask:{userId}:{(isAction ? "action" : "menu")}";
    }
}
