namespace Viv.Contracts.Models
{
    /// <summary>
    /// Viv 主体（数据域）的固定约定值。
    ///
    /// <see cref="VivClaimTypes.SubjectId"/> 是「数据隔离标识」，0 表示「确实没有主体」——
    /// 匿名调用、后台任务、未登录。平台（运营侧）必须有一个非 0 的固定归属 Id，
    /// 否则只能借用 0：0 在租户过滤器里会被拼成 <c>TenantId == 0</c>，
    /// 在能力白名单里又会匹配所有无主体的调用，两处都不是「平台」的语义。
    ///
    /// 放在 Contracts 而不是 Apex 侧，是因为 Viv.Momo 的数据过滤器也要用它，
    /// 而依赖方向是 Apex.Core → Viv.Entity → Viv.Momo，下层不能反向引用上层。
    /// </summary>
    public static class VivSubject
    {
        /// <summary>
        /// 平台主体 Id。11 位数，与雪花主键（18–19 位）永不冲突，也一眼认得出是约定值。
        /// 它不落在 AtTenant 里 —— 只是给「平台」这一侧一个可引用、可写进授权规则的 Id。
        /// </summary>
        public const long PlatformId = 12345678910L;
    }
}
