using Viv.Apex.Core.Saga.Messages;
using Viv.Nana.Saga;
using Wolverine.Persistence.Sagas;

namespace Viv.Apex.Core.Saga
{
    /// <summary>
    /// 订单 Saga — Wolverine 模型：saga 类即持久化状态（不再拆分 State + StateMachine）。
    /// 关联字段 <see cref="OrderId"/>（[SagaIdentity]）对应消息里的 OrderId 属性，
    /// 跨消息（OrderSubmitted / StockReserved / PaymentCompleted / CancelOrder）按同一订单关联。
    /// 持久化由 VivSagaDbContext 建表（Saga_OrderSaga），乐观并发由 Saga.Version 控制。
    /// </summary>
    public class OrderSaga : VivSagaState
    {
        /// <summary>
        /// 订单标识（Saga 关联键）
        /// </summary>
        [SagaIdentity]
        public Guid OrderId { get; set; }

        /// <summary>
        /// 订单金额
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// 失败原因（取消时填充）
        /// </summary>
        public string? FailedReason { get; set; }

        /// <summary>
        /// 创建 Saga：订单已创建（对应原 Initially）
        /// </summary>
        public static OrderSaga Start(OrderSubmitted message)
        {
            return new OrderSaga
            {
                OrderId = message.OrderId,
                Amount = message.Amount,
                AppId = message.AppId,
                TenantId = message.TenantId
            };
        }

        /// <summary>
        /// 库存已预留（对应原 During(Submitted)）
        /// </summary>
        public void Handle(StockReserved message)
        {
            // 状态流转：saga 已存在即代表进入「库存已预留」，如需记录可在属性上体现
        }

        /// <summary>
        /// 支付已完成 → 结束 Saga（对应原 During(StockReserved).Finalize）
        /// </summary>
        public void Handle(PaymentCompleted message)
        {
            MarkCompleted();
        }

        /// <summary>
        /// 取消订单（补偿）→ 记录失败原因并结束
        /// </summary>
        public void Handle(CancelOrder message)
        {
            FailedReason = message.Reason;
            MarkCompleted();
        }
    }
}
