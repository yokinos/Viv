using MassTransit;

namespace Viv.Nana.Saga
{
    public abstract class VivSagaState : SagaStateMachineInstance
    {
        /// <summary>
        /// MassTransit 要求的 Guid 类型关联标识（框架内部使用）
        /// </summary>
        public Guid CorrelationId { get; set; }

        public int CurrentState { get; set; }

        /// <summary>
        /// 发起分布式事务的客户端 AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 多租户隔离标识
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 乐观并发控制（用作 SQL RowVersion）
        /// </summary>
        public uint RowVersion { get; set; }
    }
}

