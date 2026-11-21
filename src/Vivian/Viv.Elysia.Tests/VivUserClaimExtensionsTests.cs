using System.Collections.Generic;
using System.Security.Claims;
using Viv.Elysia;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// 读用户 claim 的统一入口。重点是**多值 claim 必须用 FindAll**：
    /// 一人多角色时 <c>FindFirstValue(ClaimTypes.Role)</c> 只拿到第一个，
    /// 判「有没有某角色」会静默出错，而单人单角色时测不出来。
    /// </summary>
    public class VivUserClaimExtensionsTests
    {
        private static ClaimsPrincipal Principal(params Claim[] claims)
            => new(new ClaimsIdentity(claims, "test"));

        [Fact]
        public void 多角色_全部读到()
        {
            var user = Principal(
                new Claim(ClaimTypes.Role, "org_admin"),
                new Claim(ClaimTypes.Role, "auditor"));

            Assert.Equal(new[] { "org_admin", "auditor" }, user.GetRoleCodes());
            Assert.True(user.HasRoleCode("auditor"));
            Assert.False(user.HasRoleCode("admin"));
        }

        [Fact]
        public void 多角色_第一个不是目标角色也能命中()
        {
            // FindFirstValue 只返回第一个 claim —— 这条就是钉「别用它」
            var user = Principal(
                new Claim(ClaimTypes.Role, "auditor"),
                new Claim(ClaimTypes.Role, "org_admin"));

            Assert.True(user.HasRoleCode("org_admin"));
        }

        [Fact]
        public void 重复角色_去重()
        {
            var user = Principal(
                new Claim(ClaimTypes.Role, "org_admin"),
                new Claim(ClaimTypes.Role, "org_admin"));

            Assert.Single(user.GetRoleCodes());
        }

        [Fact]
        public void 未认证_角色为空_类型为null()
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.Empty(anonymous.GetRoleCodes());
            Assert.False(anonymous.HasRoleCode("org_admin"));
            Assert.Null(anonymous.GetUserType());
        }

        [Fact]
        public void 用户类型_读到数字()
        {
            var user = Principal(new Claim(VivUserClaimTypes.UserType, "2"));

            Assert.Equal(2, user.GetUserType());
        }

        [Fact]
        public void 用户类型为0_不等于缺席()
        {
            // Master = 0 是合法值，不能靠「> 0」判断有没有
            var user = Principal(new Claim(VivUserClaimTypes.UserType, "0"));

            Assert.Equal(0, user.GetUserType());
        }

        [Fact]
        public void 用户类型不是整数_当缺席()
        {
            var user = Principal(new Claim(VivUserClaimTypes.UserType, "org"));

            Assert.Null(user.GetUserType());
        }

        [Fact]
        public void 空角色编码_不算角色()
        {
            var user = Principal(
                new Claim(ClaimTypes.Role, "  "),
                new Claim(ClaimTypes.Role, "auditor"));

            var codes = user.GetRoleCodes();

            Assert.Equal(new[] { "auditor" }, codes);
            Assert.False(user.HasRoleCode(""));
            Assert.False(user.HasRoleCode(null));
        }
    }
}
