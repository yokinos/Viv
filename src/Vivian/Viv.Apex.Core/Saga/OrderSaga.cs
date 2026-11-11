using Viv.Apex.Core.Saga.Messages;
using Viv.Nana.Saga;
using Wolverine.Persistence.Sagas;

namespace Viv.Apex.Core.Saga
{
    /// <summary>
    /// 订单 Saga —— Wolverine 模型：saga 类即持久化状态（不再拆 State + StateMachine）。
    ///
    /// 关联字段 <see cref="OrderId"/>（<c>[SagaIdentity]</c>）对应消息里的 OrderId 属性，
    /// 跨消息（OrderSubmitted / StockReserved / PaymentCompleted / CancelOrder）按同一订单关联。
    /// 表由 <c>VivSagaTableCreator</c> 在启动时补建（SqlServer: Saga_OrderSaga，PG: saga_order_saga）。
    ///
    /// ⚠️ 这是**示例骨架**，目前全仓没有任何地方发布这几个消息，它不会真的跑起来。
    /// 照着抄之前先补三样东西，否则它只是个「收到消息 → 改字段」的空壳：
    /// <list type="bullet">
    /// <item>状态字段。现在没有任何列能回答「这单走到哪一步」—— 靠「saga 存在」隐含表达阶段，
    /// 遇到重复投递或乱序（PaymentCompleted 先于 StockReserved 到达）就没有防护。</item>
    /// <item>超时。ReserveStock 发出去没回音，saga 会永久挂着（Wolverine 的 saga 不会自己过期）。</item>
    /// <item>阶段守卫。<see cref="CancelOrder"/> 现在在任何阶段都能收尾，包括已支付之后。</item>
    /// </list>
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
                SubjectId = message.TenantId
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
