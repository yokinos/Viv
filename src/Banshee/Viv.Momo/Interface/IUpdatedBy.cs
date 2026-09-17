namespace Viv.Momo.Interface
{
    /// <summary>
    /// 更新人审计能力 —— 每次更新时由 <c>MomoDatabase</c> 自动盖 <c>IVivContext.UserId</c>。
    ///
    /// <para>口径同 <see cref="ICreatedBy"/>：取登录用户（非租户主体），无上下文时记 <c>null</c> 而非 <c>0</c>。</para>
    /// </summary>
    public interface IUpdatedBy
    {
        long? UpdatedBy { get; set; }
    }
}
