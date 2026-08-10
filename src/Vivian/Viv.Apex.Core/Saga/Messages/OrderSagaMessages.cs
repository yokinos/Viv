namespace Viv.Apex.Core.Saga.Messages
{
    /// <summary>
    /// 事件：订单已创建（开启 Saga）
    /// </summary>
    public class OrderSubmitted
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public long AppId { get; set; }
        public long TenantId { get; set; }
    }

    /// <summary>
    /// 事件：库存已预留
    /// </summary>
    public class StockReserved
    {
        public Guid OrderId { get; set; }
    }

    /// <summary>
    /// 事件：支付已完成
    /// </summary>
    public class PaymentCompleted
    {
        public Guid OrderId { get; set; }
    }

    /// <summary>
    /// 命令：预留库存
    /// </summary>
    public class ReserveStock
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 命令：处理支付
    /// </summary>
    public class ProcessPayment
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 命令：取消订单（补偿）
    /// </summary>
    public class CancelOrder
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
