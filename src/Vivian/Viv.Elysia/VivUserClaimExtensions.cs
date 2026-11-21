using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;

namespace Viv.Elysia
{
    /// <summary>
    /// 读 <see cref="VivUserClaimTypes"/> 的统一入口。业务、特性、测试都走这里，不要各写一套。
    ///
    /// <para>存在的理由主要是第二条：**多值 claim 必须用 <c>FindAll</c>**。一人多角色时
    /// <c>FindFirstValue(ClaimTypes.Role)</c> 只返回第一个，判「有没有某角色」会静默出错 ——
    /// 而且单人单角色时永远测不出来。</para>
    /// </summary>
    public static class VivUserClaimExtensions
    {
        /// <summary>
        /// 用户 Id（token 的 <c>NameIdentifier</c>，.NET 10 会把 <c>sub</c> 映射到它；两条都认）。
        /// 未认证 / 解析不出 / 非正数返回 <c>null</c>。
        /// </summary>
        public static long? GetUserId(this ClaimsPrincipal? user)
        {
            if (!IsAuthenticated(user))
            {
                return null;
            }

            var raw = user!.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : null;
        }

        /// <summary>用户类型；未认证 / claim 缺席 / 不是整数都返回 <c>null</c>（不是 0）。</summary>
        public static int? GetUserType(this ClaimsPrincipal? user)
        {
            if (!IsAuthenticated(user))
            {
                return null;
            }

            var raw = user!.FindFirstValue(VivUserClaimTypes.UserType);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        /// <summary>角色编码集合；未认证返回空集合，重复项已去掉（顺序不保证，别依赖）。</summary>
        public static IReadOnlyList<string> GetRoleCodes(this ClaimsPrincipal? user)
        {
            if (!IsAuthenticated(user))
            {
                return Array.Empty<string>();
            }

            return user!.FindAll(VivUserClaimTypes.RoleCode)
                .Select(static claim => claim.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>是否持有某个角色编码（按序数精确匹配，大小写敏感）。</summary>
        public static bool HasRoleCode(this ClaimsPrincipal? user, string? roleCode)
            => !string.IsNullOrWhiteSpace(roleCode)
               && user.GetRoleCodes().Contains(roleCode, StringComparer.Ordinal);

        private static bool IsAuthenticated(ClaimsPrincipal? user)
            => user?.Identity?.IsAuthenticated == true;
    }
}
