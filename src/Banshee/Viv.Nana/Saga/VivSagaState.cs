using Wolverine;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 状态基类 — Wolverine 模型下 saga 类即状态（class extends Saga）。
    /// 子类用 <see cref="Wolverine.Persistence.Sagas.SagaIdentityAttribute"/> 标记关联字段，
    /// 例如订单 Saga 用 [SagaIdentity] public Guid OrderId，对应消息里的 OrderId 属性。
    /// Wolverine 自带乐观并发控制（Saga.Version，映射为 RowVersion）。
    /// </summary>
    public abstract class VivSagaState : Wolverine.Saga
    {
        /// <summary>
        /// 发起分布式事务的客户端 AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 数据隔离标识
        /// </summary>
        public long SubjectId { get; set; }
    }
}
