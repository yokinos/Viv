using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Viv.Delusion.Magic;
using Viv.Momo;
using Viv.Momo.Enums;
using Wolverine.Persistence.Sagas;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 持久化上下文 —— 所有业务 Saga 状态统一存于此。
    ///
    /// <para>
    /// <b>这里只做映射，不建表。</b>EF 的 <c>ToTable</c> / <c>HasKey</c> 是模型元数据，
    /// 不会产生任何 DDL；表由 <see cref="VivSagaTableCreator"/> 在启动时补齐（缺哪张建哪张）。
    /// 漏了那一步的表现是「第一条 saga 消息 → 重试几次 → 死信」，从死信内容看不出是建表问题。
    /// </para>
    ///
    /// <para>
    /// 表名走 <c>dialect.ToPhysical($"Saga_{类名}")</c>：SqlServer 下 <c>Saga_OrderSaga</c>，
    /// PostgreSQL 下 <c>saga_order_saga</c>。不能直接拼 <c>$"Saga_{类名}"</c> —— PG 的
    /// <c>Quote</c> 是直通不加引号，驼峰会被折叠成全小写，和 EF 记的表名对不上。
    /// </para>
    ///
    /// <para>
    /// 主键取 <c>[SagaIdentity]</c> 标记的属性（如 <c>OrderSaga.OrderId</c>）—— EF 无法从
    /// <c>Wolverine.Saga</c> 推断主键，不显式配置会抛 "requires a primary key"，
    /// 进而让 Wolverine 判定这个 Saga 没有 EF 持久化提供者。
    /// </para>
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

            // 表名跟着方言走。PG 的 PostgresDialect.Quote 是直通（不加引号），
            // 表名留成 Saga_OrderSaga 会被 PG 折叠成全小写 saga_ordersaga，
            // 而 EF 之后按原名去找就对不上。走 ToPhysical 统一成 snake_case 才两边一致。
            var source = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                ? DatabaseSourceType.PostgreSQL
                : DatabaseSourceType.SqlServer;
            var dialect = SqlMagic.For(source);

            // 扫描所有已加载程序集中的 VivSagaState 子类（如 Apex 的 OrderSaga）→ 建表映射
            var sagaTypes = TypeScanMagic.ScanTypes<VivSagaState>();
            foreach (var type in sagaTypes)
            {
                var entity = modelBuilder.Entity(type).ToTable(dialect.ToPhysical($"Saga_{type.Name}"));

                // 主键 = [SagaIdentity] 标记的关联字段（跨消息按同一 Saga 关联的那一列）
                var identityProp = type
                    .GetProperties()
                    .FirstOrDefault(p => p.GetCustomAttribute<SagaIdentityAttribute>() != null);
                if (identityProp != null)
                {
                    entity.HasKey(identityProp.Name);
                }

                // 乐观并发。只配 HasKey 的话 Saga.Version 就是一列普通 int —— 两个消费者同时
                // 处理同一订单的 saga 会互相覆盖，而且什么都不报（丢更新最难查）。
                // 标成并发令牌后 EF 会把它放进 UPDATE 的 WHERE 条件，冲突时抛 DbUpdateConcurrencyException，
                // 由 Wolverine 的失败策略重试。
                entity.Property(nameof(Wolverine.Saga.Version)).IsConcurrencyToken();
            }
        }
    }
}
