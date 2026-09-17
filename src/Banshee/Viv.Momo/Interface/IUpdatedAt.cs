namespace Viv.Momo.Interface
{
    /// <summary>
    /// 更新时间审计能力，每次更新时由 <c>MomoDatabase</c> 自动盖章。
    /// </summary>
    public interface IUpdatedAt
    {
        DateTime? UpdatedAt { get; set; }
    }
}
