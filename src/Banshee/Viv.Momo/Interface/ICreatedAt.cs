namespace Viv.Momo.Interface
{
    /// <summary>
    /// 创建时间审计能力 —— 新增时由 <c>MomoDatabase</c> 自动盖 <see cref="DateTime.UtcNow"/>。
    ///
    /// <para>
    /// 审计字段<b>按能力逐个 opt-in</b>：实体要几个实现几个，不实现的一律不碰。
    /// 这是四个单字段接口而不是一个 <c>IAudited</c> 的原因 —— 不是所有实体都要完整四件套
    /// （纯日志表可能只要创建时间，关系表可能只要创建人）。同族的还有
    /// <see cref="ICreatedBy"/> / <see cref="IUpdatedAt"/> / <see cref="IUpdatedBy"/>。
    /// </para>
    ///
    /// <para>
    /// ⚠️ 属性类型必须<b>逐字</b>是 <c>DateTime?</c>：C# 要求接口实现者与接口的属性类型完全相同，
    /// 非空 <c>DateTime</c> 会 CS0738 编译不过。可空是<b>有意的</b> —— 行可能是手工 SQL / 导入 /
    /// DB 默认值造出来的，这时 <c>null</c> 比 <c>0001-01-01</c> 更容易被发现。
    /// </para>
    /// </summary>
    public interface ICreatedAt
    {
        DateTime? CreatedAt { get; set; }
    }
}
