using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Viv.Delusion.Magic;
using Wolverine.Persistence.Sagas;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 持久化上下文 — 所有业务 Saga 状态统一存于此
    /// OnModelCreating 扫描 VivSagaState 子类并建表映射（表名：Saga_{类型名}）
    /// 主键取 [SagaIdentity] 标记的属性（如 OrderSaga.OrderId）——EF 无法从 Saga 类型推断 PK，
    /// 不显式配置会抛 "requires a primary key"，导致 Wolverine 判定该 Saga 无 EF 持久化提供者。
    /// </summary>
    public class VivSagaDbContext : DbContext
    {
        public VivSagaDbContext(DbContextOptions<VivSagaDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 扫描所有已加载程序集中的 VivSagaState 子类（如 Apex 的 OrderSaga）→ 建表映射
            var sagaTypes = TypeScanMagic.ScanTypes<VivSagaState>();
            foreach (var type in sagaTypes)
            {
                var entity = modelBuilder.Entity(type).ToTable($"Saga_{type.Name}");

                // 主键 = [SagaIdentity] 标记的关联字段（跨消息按同一 Saga 关联的那一列）
                var identityProp = type
                    .GetProperties()
                    .FirstOrDefault(p => p.GetCustomAttribute<SagaIdentityAttribute>() != null);
                if (identityProp != null)
                {
                    entity.HasKey(identityProp.Name);
                }
            }
        }
    }
}
