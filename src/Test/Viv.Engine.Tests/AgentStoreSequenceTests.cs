using Viv.Delusion.Magic;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Ouroboros.Core.Service;

namespace Viv.Engine.Tests;

/// <summary>
/// 消息序号分配。必须跑真库：这里的正确性一半在 SQL 语法（OUTPUT / RETURNING），
/// 一半在数据库给的行锁语义上，桩不出来。
///
/// 老实现是「把这个会话的消息全查回来取 max+1」，两个并发请求会拿到同一个号，
/// 而且长会话每发一条消息都要读回全部历史。
///
/// 取号语句对两种方言各有一个分支，所以断言只写一遍，由两个子类分别绑到 Postgres / SQL Server 的夹具上。
/// </summary>
public abstract class AgentStoreSequenceTestsBase
{
    private readonly IEngineDatabaseFixture _fx;

    protected AgentStoreSequenceTestsBase(IEngineDatabaseFixture fx)
    {
        _fx = fx;
    }

    [DockerFact]
    public async Task 并发取号不重号()
    {
        using var db = CreateDb();
        await db.SyncTableAsync();

        var conversationId = await InsertConversationAsync(db);

        // 每个「副本」各拿一个上下文，模拟多实例并发命中同一条会话
        var seqs = await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
        {
            using var worker = CreateDb();
            var store = new AgentStore(worker, new RecordingLogger());
            return await store.NextSeqAsync(conversationId);
        }));

        Assert.Equal(30, seqs.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 30), seqs.OrderBy(x => x));
    }

    [DockerFact]
    public async Task 会话不存在时抛异常而不是返回1()
    {
        using var db = CreateDb();
        await db.SyncTableAsync();

        var store = new AgentStore(db, new RecordingLogger());

        // 默默返回 1 的话，调用方会拿它写进消息表，和那个会话已有的消息撞号，
        // 而且错得没有任何痕迹
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.NextSeqAsync(999_999));
    }

    private MomoDatabaseContext CreateDb()
    {
        var options = new DatabaseOptions
        {
            DatabaseSource = _fx.Source,
            MasterConnectionString = _fx.ConnectionString,
            Timeout = 30,
            EntityTypeOptions =
            [
                new FilterTypeOptions
                {
                    AssemblyName = "Viv.Entity",
                    Namespace = "Viv.Entity.Database.Ouroboros",
                }
            ]
        };

        return new MomoDatabaseContext(new TestContext(), new RecordingLogger(), new StaticOptions(options));
    }

    private static async Task<long> InsertConversationAsync(MomoDatabaseContext db)
    {
        var conversation = new OtConversation
        {
            ConversationKey = Guid.NewGuid(),
            MainAgentKey = "main",
            Status = EmConversationStatus.Active
        };

        Assert.True(await db.InsertAsync(conversation));
        Assert.True(conversation.Id > 0);
        return conversation.Id;
    }

    private sealed class StaticOptions : IDatabaseOptionsProvider
    {
        private readonly DatabaseOptions _options;

        public StaticOptions(DatabaseOptions options)
        {
            _options = options;
        }

        public DatabaseOptions GetRealOptions() => _options;
    }
}

/// <summary>RETURNING 分支。</summary>
[Collection("EnginePostgres")]
public class AgentStoreSequencePostgresTests(EnginePostgresFixture fx) : AgentStoreSequenceTestsBase(fx);

/// <summary>OUTPUT INSERTED 分支。SQL Server 的 OUTPUT 必须夹在 SET 和 WHERE 之间，
/// 写错位置语法就报错，所以这一支必须真跑。</summary>
[Collection("EngineSqlServer")]
public class AgentStoreSequenceSqlServerTests(EngineSqlServerFixture fx) : AgentStoreSequenceTestsBase(fx);
