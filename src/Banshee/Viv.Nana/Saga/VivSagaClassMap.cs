using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 映射基类 — 统一配置 CorrelationId 主键 + RowVersion 乐观并发
    /// </summary>
    public abstract class VivSagaClassMap<TSaga> : SagaClassMap<TSaga> where TSaga : VivSagaState
    {
        protected override void Configure(EntityTypeBuilder<TSaga> entity, ModelBuilder model)
        {
            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.CurrentState);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.AppId);
            entity.HasIndex(x => x.TenantId);
        }
    }
}
