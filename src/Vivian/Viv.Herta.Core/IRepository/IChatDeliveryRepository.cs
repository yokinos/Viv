namespace Viv.Herta.Core.IRepository
{
    /// <summary>
    /// 投递水位仓储：每个用户一行，Ack 推水位、Resync 读水位。
    /// 存在库里而不是只放 Redis，重装/换 Redis 之后客户端还能知道自己收到哪儿。
    /// </summary>
    public interface IChatDeliveryRepository
    {
        /// <summary>
        /// 把水位推到 <paramref name="messageId"/>（只进不退），返回落库后的水位。
        /// 行不存在时插入。
        /// </summary>
        Task<long> MergeAsync(long tenantId, long userId, long messageId);

        /// <summary>读当前水位，没有记录返回 null</summary>
        Task<long?> GetAsync(long tenantId, long userId);
    }
}
