namespace Viv.Momo.Interface
{
    /// <summary>
    /// 更新人审计能力，每次更新时由 <c>MomoDatabase</c> 盖 <c>IVivContext.UserId</c>。
    /// 口径同 <see cref="ICreatedBy"/>。
    /// </summary>
    public interface IUpdatedBy
    {
        long? UpdatedBy { get; set; }
    }
}
