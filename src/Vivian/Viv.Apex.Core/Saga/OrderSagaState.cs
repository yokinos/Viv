using Viv.Nana.Saga;

namespace Viv.Apex.Core.Saga
{
    /// <summary>
    /// 订单 Saga 持久化状态
    /// </summary>
    public class OrderSagaState : VivSagaState
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? FailedReason { get; set; }
    }
}
