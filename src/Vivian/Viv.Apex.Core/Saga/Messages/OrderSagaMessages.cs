namespace Viv.Apex.Core.Saga.Messages
{
    /// <summary>
    /// 事件：订单已创建
    /// </summary>
    public interface OrderSubmitted
    {
        Guid OrderId { get; }
        decimal Amount { get; }
        long AppId { get; }
        long TenantId { get; }
    }

    /// <summary>
    /// 事件：库存已预留
    /// </summary>
    public interface StockReserved
    {
        Guid OrderId { get; }
    }

    /// <summary>
    /// 事件：支付已完成
    /// </summary>
    public interface PaymentCompleted
    {
        Guid OrderId { get; }
    }

    /// <summary>
    /// 命令：预留库存
    /// </summary>
    public interface ReserveStock
    {
        Guid OrderId { get; }
        decimal Amount { get; }
    }

    /// <summary>
    /// 命令：处理支付
    /// </summary>
    public interface ProcessPayment
    {
        Guid OrderId { get; }
        decimal Amount { get; }
    }

    /// <summary>
    /// 命令：取消订单（补偿）
    /// </summary>
    public interface CancelOrder
    {
        Guid OrderId { get; }
        string Reason { get; }
    }
}
