using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Viv.Momo.Enums;
using Viv.Nana.Saga;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence;
using Wolverine.Persistence.Sagas;

namespace Viv.Nana.Tests;

/// <summary>
/// Saga 持久化的端到端验证：起真的 SQL Server、走真实的消息管线、只看库里的行。
///
/// 它补的是 <see cref="VivSagaTableCreatorTests"/> 那类纯字符串断言够不着的两件事：
/// <list type="number">
/// <item>Wolverine 到底把 saga 存到哪。只在 DI 里注册 DbContext、漏了
/// <c>AddDbContextWithWolverineIntegration</c> 时，saga 会静默存进内存 ——
/// 任何「注册成功」的断言都看不出来，只有查库能分辨。</item>
/// <item>跨消息关联是否真的生效。第二条消息能不能按 <c>[SagaIdentity]</c> 找回第一条创建的那一行。</item>
/// </list>
///
/// 默认跳过（见 <see cref="DockerFactAttribute"/>），要跑：
/// <c>VIV_CONTAINER_TESTS=1 dotnet test src/Test/Viv.Nana.Tests</c>
/// </summary>
public class VivSagaPersistenceTests
{
    [DockerFact]
    public async Task Saga落库_跨消息关联_完成后删行()
    {
        await using var container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Viv_Sql_77!")
            .Build();
        await container.StartAsync();

        var connectionString = container.GetConnectionString();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContextWithWolverineIntegration<VivSagaDbContext>(
            x => x.UseSqlServer(connectionString));
        builder.UseWolverine(opts =>
        {
            opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Lightweight);
            opts.Discovery.IncludeType<FlowSaga>();
        });

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            // 建表由建表器负责 —— 这一步同时验证「表没人建」那个坑已经堵上
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VivSagaDbContext>();
                var created = await VivSagaTableCreator.EnsureTablesAsync(
                    db, DatabaseSourceType.SqlServer, connectionString);

                Assert.Contains("Saga_FlowSaga", created);
            }

            var id = Guid.NewGuid();
            var bus = host.Services.GetRequiredService<IMessageBus>();

            // ① 第一条消息创建 saga。内存存储在这里查不到行 —— 这是本测试的核心断言。
            await bus.InvokeAsync(new FlowStarted { Id = id, AppId = 11L, TenantId = 22L });

            var started = await FindAsync(host, id);
            Assert.NotNull(started);
            Assert.Equal("started", started!.Stage);
            Assert.Equal(11L, started.AppId);
            Assert.Equal(22L, started.SubjectId);

            // ② 第二条消息按 [SagaIdentity] 找回同一行并改状态
            await bus.InvokeAsync(new FlowAdvanced { Id = id, Stage = "reserved" });
            Assert.Equal("reserved", (await FindAsync(host, id))!.Stage);

            // ③ MarkCompleted → Wolverine 删掉这一行
            await bus.InvokeAsync(new FlowFinished { Id = id, Reason = "done" });
            Assert.Null(await FindAsync(host, id));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<FlowSaga?> FindAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VivSagaDbContext>();

        // AsNoTracking：不要命中 EF 的身份映射缓存，否则读到的可能是内存里那份而不是库里的行
        return await db.Set<FlowSaga>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    /// <summary>
    /// 测试专用 saga：一个「创建 → 推进 → 收尾」的最小流程。
    /// 消息类保持 public setter —— 项目硬约定，序列化走 System.Text.Json 默认选项。
    /// </summary>
    public class FlowSaga : VivSagaState
    {
        [SagaIdentity]
        public Guid Id { get; set; }

        public string Stage { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public static FlowSaga Start(FlowStarted message)
            => new() { Id = message.Id, Stage = "started", AppId = message.AppId, SubjectId = message.TenantId };

        public void Handle(FlowAdvanced message) => Stage = message.Stage;

        public void Handle(FlowFinished message)
        {
            Reason = message.Reason;
            MarkCompleted();
        }
    }

    public class FlowStarted
    {
        public Guid Id { get; set; }
        public long AppId { get; set; }
        public long TenantId { get; set; }
    }

    public class FlowAdvanced
    {
        public Guid Id { get; set; }
        public string Stage { get; set; } = string.Empty;
    }

    public class FlowFinished
    {
        public Guid Id { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
