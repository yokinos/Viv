using MassTransit;

namespace Viv.Nana.Saga
{
    public abstract class VivSagaState : SagaStateMachineInstance
    {
        /// <summary>
        /// 分布式事务的标识
        /// </summary>
        public Guid CorrelationId { get; set; }

        /// <summary>
        /// 分布式事务当前状态
        /// </summary>
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

