using System.Threading;
using System.Threading.Tasks;

namespace Viv.Elysia.Interface
{
    /// <summary>
    /// 用户权限点判定。特性（<c>[VivRequirePermission]</c>）只负责问，具体怎么查由应用层实现。
    ///
    /// <para>为什么不写在框架层：权限点、角色、授权关系全是业务数据（Apex 的
    /// <c>AtUserRole</c> / <c>AtRoleAction</c> / <c>AtMenuAction</c>），Viv.Contracts / Viv.Engine
    /// 不该认识它们。Elysia 只留这个接口 + 特性，实现放在业务程序集里。</para>
    ///
    /// <para><b>实现要求</b>：必须带缓存。这个方法在<b>每个</b>带特性的请求上都会被调，
    /// 直接查库就等于给每个接口加四张表的 join。</para>
    ///
    /// <para><b>「谁可以整体放行」由实现决定</b>：框架只问「有没有」，不预置任何旁路。
    /// Apex 的实现会把超级管理员直接放过（标记和掩码一起缓存在同一份快照里，不会多一次查库），
    /// 换一个应用完全可以不给任何人旁路。</para>
    /// </summary>
    public interface IUserPermissionChecker
    {
        /// <summary>
        /// 该用户是否拥有指定动作编码（用户全部启用角色的并集）。
        /// 实现里不要抛「用户不存在」之类的业务异常 —— 没有权限就返回 false。
        /// </summary>
        Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken cancellationToken = default);
    }
}
