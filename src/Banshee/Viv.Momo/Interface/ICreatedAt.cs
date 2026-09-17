namespace Viv.Momo.Interface
{
    /// <summary>
    /// 创建时间审计能力，新增时由 <c>MomoDatabase</c> 自动盖章。
    /// 同族：<see cref="ICreatedBy"/> / <see cref="IUpdatedAt"/> / <see cref="IUpdatedBy"/>，按需逐个实现。
    /// </summary>
    public interface ICreatedAt
    {
        DateTime? CreatedAt { get; set; }
    }
}
