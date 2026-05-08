using MassTransit;
using Viv.Apex.Core.Saga.Messages;

namespace Viv.Apex.Core.Saga
{
    /// <summary>
    /// 订单 Saga 状态机：创建订单 → 预留库存 → 处理支付
    /// </summary>
    public class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
    {
        // 状态
        public State Submitted { get; private set; }
        public State StockReserved { get; private set; }
        public State Paid { get; private set; }
        public State Cancelled { get; private set; }

        // 事件
        public Event<OrderSubmitted> OrderSubmittedEvent { get; private set; }
        public Event<StockReserved> StockReservedEvent { get; private set; }
        public Event<PaymentCompleted> PaymentCompletedEvent { get; private set; }

        public OrderSagaStateMachine()
        {
            InstanceState(x => x.CurrentState);

            // 用 OrderId 做关联（MassTransit 内部仍用 CorrelationId 路由）
            Event(() => OrderSubmittedEvent, e =>
                e.CorrelateById(ctx => ctx.Message.OrderId));
            Event(() => StockReservedEvent, e =>
                e.CorrelateById(ctx => ctx.Message.OrderId));
            Event(() => PaymentCompletedEvent, e =>
                e.CorrelateById(ctx => ctx.Message.OrderId));

            Initially(
                When(OrderSubmittedEvent)
                    .Then(ctx =>
                    {
                        ctx.Saga.CorrelationId = ctx.Message.OrderId;
                        ctx.Saga.OrderId = ctx.Message.OrderId.ToString();
                        ctx.Saga.Amount = ctx.Message.Amount;
                        ctx.Saga.AppId = ctx.Message.AppId;
                        ctx.Saga.TenantId = ctx.Message.TenantId;
                    })
                    .TransitionTo(Submitted)
                    .Publish(ctx => new
                    {
                        ctx.Message.OrderId,
                        ctx.Message.Amount
                    }, ctx => ctx.ResponseAddress = default!)
            );

            During(Submitted,
                When(StockReservedEvent)
                    .TransitionTo(StockReserved)
                    .Publish(ctx => new
                    {
                        ctx.Saga.CorrelationId,
                        ctx.Saga.Amount
                    })
            );

            During(StockReserved,
                When(PaymentCompletedEvent)
                    .TransitionTo(Paid)
                    .Finalize()
            );

            // 任何非终态都可以取消
            During(Submitted, StockReserved,
                When(OrderSubmittedEvent)
                    .Then(ctx =>
                    {
                        ctx.Saga.FailedReason = "重复提交";
                    })
                    .TransitionTo(Cancelled)
                    .Finalize()
            );

            SetCompletedWhenFinalized();
        }
    }
}
