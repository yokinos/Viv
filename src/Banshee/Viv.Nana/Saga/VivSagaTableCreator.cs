using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Viv.Delusion.Magic;
using Viv.Momo;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Momo.Sync;
using Wolverine.Persistence.Sagas;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Saga 表的运行时建表器。
    ///
    /// 每个 <see cref="VivSagaState"/> 子类一张表（<c>Saga_{类名}</c>），表结构跟着子类属性走，
    /// 所以没法像 Outbox / Skirk 那样手写一份固定 DDL —— 这里复用 <see cref="SchemaSynchronizer"/>
    /// 的「类型 → 列定义 → DDL」能力，只补缺失的表。
    ///
    /// 为什么不用 EF 的 <c>EnsureCreated</c>：它只在库里一张表都没有时才建，之后新增一个 saga 类
    /// 不会再补；也没有走 Migrations —— 这个库只放 saga 状态，不需要版本演进。
    /// </summary>
    public static class VivSagaTableCreator
    {
        /// <summary>
        /// 确保每个 saga 类型的表都存在，返回本次新建的表名（已存在的表不动）。
        ///
        /// 只建表，不做 ALTER / DROP：<see cref="SchemaSynchronizer.GenerateDdl"/> 会为「库里多余的表」
        /// 生成 <c>DROP TABLE</c>，而实际表结构是查**整库**得来的 —— saga 库里要是还躺着别的表
        /// （Wolverine 的 envelope、手工建的辅助表），整份 diff 交给它就会被判成多余然后删掉。
        /// </summary>
        /// <param name="context">saga 持久化上下文，DDL 借它执行（复用 DI 里的连接配置）</param>
        /// <param name="source">数据库类型，决定 DDL 方言</param>
        /// <param name="connectionString">saga 库连接串，用于读取库里已有哪些表</param>
        public static async Task<IReadOnlyList<string>> EnsureTablesAsync(
            VivSagaDbContext context,
            DatabaseSourceType source,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            var sagaTypes = TypeScanMagic.ScanTypes<VivSagaState>();
            if (sagaTypes.Count == 0)
            {
                return [];
            }

            var synchronizer = new SchemaSynchronizer(new DatabaseOptions
            {
                DatabaseSource = source,
                MasterConnectionString = connectionString,
            });

            var expected = BuildExpectedTables(sagaTypes, source, synchronizer);
            var actual = await synchronizer.FetchActualSchemaAsync(cancellationToken).ConfigureAwait(false);

            var diff = synchronizer.Diff(expected, actual);
            if (diff.NewTables.Count == 0)
            {
                return [];
            }

            var createOnly = new SyncDiffResult { NewTables = diff.NewTables };
            foreach (var sql in synchronizer.GenerateDdl(createOnly))
            {
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }

            return diff.NewTables.Select(t => t.TableName).ToList();
        }

        /// <summary>
        /// 把 saga 类型翻译成「期望的表结构」。<see cref="SchemaSynchronizer.BuildExpectedSchema"/> 返回的
        /// 顺序与入参一致，这里按下标把两处对不上的地方掰回来：表名和主键。
        ///
        /// internal 是为了让单元测试能在不连数据库的前提下断言生成的 DDL（见 VivSagaTableCreatorTests）。
        /// </summary>
        internal static List<TableInfo> BuildExpectedTables(
            List<Type> sagaTypes,
            DatabaseSourceType source,
            SchemaSynchronizer synchronizer)
        {
            var dialect = SqlMagic.For(source);
            var tables = synchronizer.BuildExpectedSchema(sagaTypes);

            for (var i = 0; i < tables.Count; i++)
            {
                var sagaType = sagaTypes[i];
                var table = tables[i];

                // 表名：必须和 VivSagaDbContext.OnModelCreating 里的 ToTable 算出同一个值 ——
                // 都走 dialect.ToPhysical，PG 下是 saga_order_saga（PG 不加引号，驼峰会被折叠成小写），
                // SqlServer 下是 Saga_OrderSaga。
                table.Name = dialect.ToPhysical($"Saga_{sagaType.Name}");

                // 主键：它只认 [Key] 或名字叫 Id 的属性，而 saga 的主键是 [SagaIdentity] 标记的那个。
                // 找不到就别建了 —— 建一张没有主键的表，EF 那边 HasKey 也没配，两边都用不了。
                var identity = sagaType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(p => p.GetCustomAttribute<SagaIdentityAttribute>() is not null);

                if (identity is null)
                {
                    throw new InvalidOperationException(
                        $"Saga 类型 {sagaType.Name} 没有 [SagaIdentity] 标记的属性，无法确定主键，拒绝建表。");
                }

                var identityColumn = dialect.ToPhysical(identity.Name);
                foreach (var column in table.Columns)
                {
                    column.IsPrimaryKey = string.Equals(column.Name, identityColumn, StringComparison.OrdinalIgnoreCase);
                }
            }

            return tables;
        }
    }
}
