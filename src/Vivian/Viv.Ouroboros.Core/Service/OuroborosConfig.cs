using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Log;
using Viv.Momo;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// <see cref="IOuroborosConfig"/> 实现：读 <c>OtConfig</c>，结果进内存缓存。
    ///
    /// 缓存必须存在：订阅方（每轮对话、每次工具调用）都在热路径上，逐次查库会把"配置读取"
    /// 变成每轮的固定开销；而这些键都是"改一次用很久"的旋钮，60 秒延迟无感。
    /// 缓存里存的是**原文**而不是解析后的整数：0 是合法配置值（表示关闭），
    /// 用 0 兼作"没命中"会把"关掉裁剪"变成每次都查库。
    ///
    /// 单例 + <see cref="IServiceScopeFactory"/>：<c>IMomoDbContext</c> 是 Scoped，
    /// 而调用方里有被缓存的聊天客户端，钉住注入就等于钉住一个早就释放的作用域。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public class OuroborosConfig : IOuroborosConfig, IDependency
    {
        /// <summary>OtConfig 键：每个会话最多保留多少轮上下文</summary>
        public const string MaxRetainedTurnsKey = "Conversation.MaxRetainedTurns";

        /// <summary>OtConfig 键：单个工具结果回给模型的最大字符数</summary>
        public const string ToolResultMaxCharsKey = "Tool.ResultMaxChars";

        /// <summary>
        /// 缺省保留 20 轮。缺省**必须裁剪**而不是放行：不裁剪的失败形态是硬失败
        /// （上下文超限，整段会话直接报错），裁早了的失败形态只是记忆变差，
        /// 护栏得默认开着；20 轮又远高于正常对话需要，正常用不会碰到。
        /// </summary>
        private const int DefaultMaxRetainedTurns = 20;

        /// <summary>缺省 8000 字符（约 2k token）。留上限是因为 HTTP 工具与子 Agent 的正文长度完全不可控</summary>
        private const int DefaultToolResultMaxChars = 8000;

        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCacheService _cache;
        private readonly IConfigVersionGate _version;
        private readonly ILoggerContract _logger;
        private long _seenGeneration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scopeFactory">作用域工厂（每次读库现开作用域取 IMomoDbContext）</param>
        /// <param name="cache">内存缓存</param>
        /// <param name="version">配置版本闸门（refresh 后立刻丢掉旧配置值）</param>
        /// <param name="logger">日志</param>
        public OuroborosConfig(IServiceScopeFactory scopeFactory, IMemoryCacheService cache,
            IConfigVersionGate version, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
            _version = version;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<int> GetMaxRetainedTurnsAsync(CancellationToken cancellationToken = default)
            => GetIntAsync(MaxRetainedTurnsKey, DefaultMaxRetainedTurns, cancellationToken);

        /// <inheritdoc />
        public Task<int> GetToolResultMaxCharsAsync(CancellationToken cancellationToken = default)
            => GetIntAsync(ToolResultMaxCharsKey, DefaultToolResultMaxChars, cancellationToken);

        /// <summary>
        /// 别的实例 refresh 过就丢掉本进程缓存的配置值 —— 否则"改完配置点 refresh"仍会拿着旧值
        /// </summary>
        private void RefreshIfConfigChanged()
        {
            var generation = _version.EnsureFresh();
            if (generation == Interlocked.Read(ref _seenGeneration)) return;

            Interlocked.Exchange(ref _seenGeneration, generation);
            _cache.Remove(CacheKey(MaxRetainedTurnsKey));
            _cache.Remove(CacheKey(ToolResultMaxCharsKey));
        }

        private async Task<int> GetIntAsync(string configKey, int fallback, CancellationToken cancellationToken)
        {
            RefreshIfConfigChanged();

            var cacheKey = CacheKey(configKey);
            // 命中即返回（含空串 = "库里没这行"），下面的解析是纯字符串操作，不需要再查库
            if (!_cache.TryGet<string>(cacheKey, out var raw))
            {
                raw = await ReadRawAsync(configKey, cancellationToken);
                _cache.Set(cacheKey, raw ?? string.Empty, CacheTime);
            }

            return Parse(raw, fallback);
        }

        /// <summary>取配置原文；行不存在、写成空，都回 null（由调用方按缺省处理）</summary>
        private async Task<string?> ReadRawAsync(string configKey, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<IMomoDbContext>();
                if (db is null) return null;

                // 键上没有唯一约束（本阶段不改表结构），重复行时取最先写入的那条，别让 SingleOrDefault 抛
                var row = await db.FirstOrDefaultAsync<OtConfig>(x => x.ConfigKey == configKey, cancellationToken);
                if (row is null || string.IsNullOrWhiteSpace(row.ConfigValue)) return null;

                if (!int.TryParse(row.ConfigValue.Trim(), out _))
                    _logger.Warning("配置项不是整数，按缺省值处理：{0}={1}", configKey, row.ConfigValue);

                return row.ConfigValue;
            }
            catch (Exception ex)
            {
                // 配置读不到只退回缺省，不能让"读配置"本身成为一轮对话失败的原因
                _logger.Warning("读取配置项失败，按缺省值处理：{0}，{1}", configKey, ex.Message);
                return null;
            }
        }

        private static int Parse(string? raw, int fallback)
            => !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var value) ? value : fallback;

        private static string CacheKey(string configKey) => $"ouroboros:config:{configKey}";
    }
}
