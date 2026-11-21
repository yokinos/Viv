using System.Security.Claims;

namespace Viv.Elysia
{
    /// <summary>
    /// 应用级用户声明 —— 只活在 JWT 里，不进框架层的身份契约。
    ///
    /// <para>用户类型、角色都是业务概念（枚举和角色表在 Viv.Entity），不该由
    /// Viv.Contracts / Viv.Engine / Keke 携带或解释。所以它们不是 <c>x-viv-*</c> 头、
    /// 不进 <c>IVivContext</c>、不参与 <c>x-request-token</c> 的签名载荷：
    /// 登录签发时写进 token，需要判定的地方从 <c>HttpContext.User</c> 直接读。</para>
    ///
    /// <para>两点代价：① 只有带 JWT 的请求判得出来（服务间调用、匿名入口、没配
    /// TokenOption 的宿主拿不到 claim，按限制语义一律拒绝）；② token 是<b>签发那一刻的快照</b>
    /// —— 改角色要等过期或刷新才生效，别把它当实时权限源。</para>
    /// </summary>
    public static class VivUserClaimTypes
    {
        /// <summary>
        /// 用户类型（<c>EmUserType</c> 的数值）。
        /// 用 <see cref="VivUserClaimExtensions.GetUserType"/> 读，别自己 <c>FindFirstValue</c>。
        /// </summary>
        public const string UserType = "UserType";

        /// <summary>
        /// 角色编码（<c>AtUserRole.RoleCode</c>），一人多角色时写多条。<b>直接用标准 claim 类型</b>
        /// （<see cref="ClaimTypes.Role"/>），这样 <c>User.IsInRole("x")</c> 和
        /// <c>[Authorize(Roles = "x")]</c> 开箱可用，不必再教一套读法。
        ///
        /// <para>为什么是编码不是主键：编码在开发/测试/生产库之间一致，日志里也能看懂；
        /// 自增 Id 换个环境含义就变了，拿它做授权判定迟早出事。</para>
        /// </summary>
        public const string RoleCode = ClaimTypes.Role;
    }
}
