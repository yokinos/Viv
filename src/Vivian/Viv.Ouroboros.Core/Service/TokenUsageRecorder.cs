using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Momo;
using Viv.Ouroboros.Core.IRepository;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// token 用量日聚合（<c>OtTokenUsageDaily</c>）的写入器：一轮主 Agent 对话、一次子 Agent 调用，
    /// 各按 <c>日期 + 主体 + Agent + 档位 + 模型</c> 五个维度累加一行。
    ///
    /// 幂等与并发安全靠**一条 SQL**（<see cref="UpsertSql"/>）：<c>MERGE ... WITH (HOLDLOCK)</c>
    /// 在目标表上取串联化范围锁，"没有就插、有就加"整个动作是原子的，
    /// 不存在"先查后插"那种两个请求都查到没有、于是插出两行的窗口，也不会把并发累加丢掉。
    /// 注意累加语义本身是**加性**的：同一个事件被重复投递会重复计数，
    /// 所以调用点只在"这一轮已经落完消息"之后调用一次，失败只记日志、不重试。
    /// 要真正的 exactly-once 需要一个去重键（唯一索引 + 事件 id），那要动表结构，本阶段没做。
    ///
    /// 单例 + <see cref="IServiceScopeFactory"/>：调用方里有被缓存的工具闭包，而
    /// <c>IMomoDbContext</c> 是 Scoped，钉住注入就等于钉住一个早就释放的作用域。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public class TokenUsageRecorder : IDependency
    {
        /// <summary>
        /// 按五个维度累加。<c>HOLDLOCK</c> 不能去掉：没有它，MERGE 的"不存在则插入"在并发下
        /// 仍会插出重复行（SQL Server 的 MERGE 需要 SERIALIZABLE 才挡住并发插入）。
        /// 维度列都可空，所以用 <c>IS NULL</c> 显式配对，别指望 <c>=</c> 能匹配 NULL。
        /// </summary>
        internal const string UpsertSql =
            """
            MERGE OtTokenUsageDaily WITH (HOLDLOCK) AS target
            USING (SELECT
                       @StatDate AS StatDate, @SubjectId AS SubjectId, @AgentKey AS AgentKey,
                       @ModelProfile AS ModelProfile, @ModelId AS ModelId, @CallCount AS CallCount,
                       @InputTokens AS InputTokens, @OutputTokens AS OutputTokens,
                       @CachedInputTokens AS CachedInputTokens, @ReasoningTokens AS ReasoningTokens,
                       @EstimatedCost AS EstimatedCost) AS source
            ON target.StatDate = source.StatDate
               AND ((target.SubjectId IS NULL AND source.SubjectId IS NULL) OR target.SubjectId = source.SubjectId)
               AND target.AgentKey = source.AgentKey
               AND ((target.ModelProfile IS NULL AND source.ModelProfile IS NULL) OR target.ModelProfile = source.ModelProfile)
               AND ((target.ModelId IS NULL AND source.ModelId IS NULL) OR target.ModelId = source.ModelId)
            WHEN MATCHED THEN UPDATE SET
                target.CallCount = target.CallCount + source.CallCount,
                target.InputTokens = target.InputTokens + source.InputTokens,
                target.OutputTokens = target.OutputTokens + source.OutputTokens,
                target.CachedInputTokens = target.CachedInputTokens + source.CachedInputTokens,
                target.ReasoningTokens = target.ReasoningTokens + source.ReasoningTokens,
                target.EstimatedCost = target.EstimatedCost + source.EstimatedCost,
                target.UpdatedAt = @Now
            WHEN NOT MATCHED THEN INSERT
                (Id, StatDate, SubjectId, AgentKey, ModelProfile, ModelId, CallCount, InputTokens,
                 OutputTokens, CachedInputTokens, ReasoningTokens, EstimatedCost, CreatedAt, UpdatedAt)
            VALUES
                (@Id, source.StatDate, source.SubjectId, source.AgentKey, source.ModelProfile, source.ModelId,
                 source.CallCount, source.InputTokens, source.OutputTokens, source.CachedInputTokens,
                 source.ReasoningTokens, source.EstimatedCost, @Now, @Now);
            """;

        /// <summary>OtAgent/OtModelProfile 查出来的维度缓存时长：档位与模型名不会一秒一变</summary>
        private static readonly TimeSpan DimensionCacheTime = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCacheService _cache;
        private readonly IConfigVersionGate _version;
        private readonly ILoggerContract _logger;
        private long _seenGeneration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scopeFactory">作用域工厂（每次落库现开作用域）</param>
        /// <param name="cache">内存缓存（维度解析结果）</param>
        /// <param name="version">配置版本闸门（refresh 后丢掉维度缓存）</param>
        /// <param name="logger">日志</param>
        public TokenUsageRecorder(IServiceScopeFactory scopeFactory, IMemoryCacheService cache,
            IConfigVersionGate version, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
            _version = version;
            _logger = logger;
        }

        /// <summary>
        /// 累加一次调用（一轮主 Agent 对话或一次子 Agent 调用）。
        /// 自身失败只记日志：出账数据不该把已经跑完的对话变成失败。
        /// </summary>
        /// <param name="agentKey">产生这次用量的 Agent 业务键（主 Agent 或子 Agent）</param>
        /// <param name="usage">MAF 回的用量；拿不到（流式、无用量供应商）传 null，只累加调用次数</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task RecordAsync(string agentKey, UsageDetails? usage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return;

            try
            {
                RefreshIfConfigChanged();

                using var scope = _scopeFactory.CreateScope();
                var provider = scope.ServiceProvider;
                var db = provider.GetService<IMomoDbContext>();
                if (db is null) return;

                var dimension = await ResolveModelAsync(provider, agentKey, cancellationToken).ConfigureAwait(false);

                await db.ExecuteSqlAsync(UpsertSql, new
                {
                    // 手写 SQL 不走框架的 Insert，Id 得自己给：OtTokenUsageDaily.Id 不是自增列、也没有默认值
                    Id = IdMagic.NextId(),
                    StatDate = DateTime.Today,
                    SubjectId = provider.GetService<IVivContext>()?.SubjectId,
                    AgentKey = agentKey,
                    dimension.ModelProfile,
                    dimension.ModelId,
                    CallCount = 1L,
                    InputTokens = (long)(usage?.InputTokenCount ?? 0),
                    OutputTokens = (long)(usage?.OutputTokenCount ?? 0),
                    CachedInputTokens = (long)(usage?.CachedInputTokenCount ?? 0),
                    ReasoningTokens = (long)(usage?.ReasoningTokenCount ?? 0),
                    // OtModelProfile 里没有单价列，算不出费用；要出账得先加列（本阶段不动表结构）
                    EstimatedCost = 0m,
                    Now = DateTime.Now
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("token 用量聚合入库失败：{0}，{1}", agentKey, ex.Message);
            }
        }

        /// <summary>
        /// 别的实例 refresh 过就让维度缓存整体作废。缓存键里带版本号而不是逐个 Remove：
        /// 每个 Agent 一个键、枚举不出来，带上版本后旧键自然再也读不到，60 秒后过期。
        /// </summary>
        private void RefreshIfConfigChanged()
        {
            var generation = _version.EnsureFresh();
            if (generation == Interlocked.Read(ref _seenGeneration)) return;

            Interlocked.Exchange(ref _seenGeneration, generation);
        }

        /// <summary>
        /// 解析出账维度里的档位与模型名：Agent → OtAgent.ModelProfile → OtModelProfile.Model。
        /// 结果按 Agent 缓存 60 秒，避免每次调用都为两个不会变的字符串查两次库。
        /// </summary>
        private async ValueTask<TokenDimension> ResolveModelAsync(IServiceProvider provider, string agentKey,
            CancellationToken cancellationToken)
        {
            var cacheKey = CacheKey(agentKey);
            if (_cache.TryGet<TokenDimension>(cacheKey, out var cached) && cached is not null) return cached;

            var dimension = await LoadDimensionAsync(provider, agentKey, cancellationToken).ConfigureAwait(false);
            _cache.Set(cacheKey, dimension, DimensionCacheTime);
            return dimension;
        }

        private async ValueTask<TokenDimension> LoadDimensionAsync(IServiceProvider provider, string agentKey,
            CancellationToken cancellationToken)
        {
            var agents = provider.GetService<IAgentRepository>();
            var profiles = provider.GetService<IModelProfileRepository>();
            if (agents is null || profiles is null) return new TokenDimension(null, null);

            var agent = await agents.GetByKeyAsync(agentKey).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(agent?.ModelProfile)) return new TokenDimension(null, null);

            var rows = await profiles.GetEnabledByKeyAsync(agent.ModelProfile).ConfigureAwait(false);
            return new TokenDimension(agent.ModelProfile, rows.Count == 0 ? null : rows[0].Model);
        }

        private string CacheKey(string agentKey)
            => $"ouroboros:usage-dim:{Interlocked.Read(ref _seenGeneration)}:{agentKey}";

        /// <summary>出账的两个"名字"维度：档位键与实际模型名</summary>
        private sealed record TokenDimension(string? ModelProfile, string? ModelId);
    }
}
