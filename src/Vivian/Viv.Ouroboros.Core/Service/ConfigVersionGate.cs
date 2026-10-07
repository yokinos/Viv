using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo;
using Viv.Ouroboros.Core.IService;
using Viv.Redis;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 版本戳实现。选版本戳而不是订阅广播：广播要维护长连接、重连与订阅生命周期，
    /// 而版本戳只用一次 SET + 一次节流 GET，漏看一次也会在下一轮自然追上，没有"永久失联"这种状态。
    ///
    /// 戳用不透明的 GUID 而不是自增数或时间戳：多实例并发 bump、机器时钟偏差都不会让它"看起来变旧"，
    /// 判等即可。共享戳丢失（Redis 重启 / 过期 / 被清）只会让各实例多清一次缓存，不影响正确性。
    ///
    /// 共享戳只覆盖"走了管理接口"的变更。<b>裸改库</b>（直接 UPDATE OtCapabilityBinding 把某条绑定
    /// 改成需审批 + 白名单）既不写戳也没有任何通知，只能由库侧指纹察觉：每
    /// <see cref="FingerprintInterval"/> 读一次四张配置表的判定列，变形就自增版本，
    /// 让各缓存（Agent 装配、工具列表）整体失效。少了它，"免审批"的那份工具列表会一直活到 60 秒 TTL，
    /// 这段时间里白名单内的主体调用需审批工具不会弹审批 —— 详见 <see cref="ToolRegistry"/>。
    ///
    /// 单例：节流窗口必须进程内唯一，否则每个 Scoped 实例各算各的，等于没节流。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public sealed class ConfigVersionGate : IConfigVersionGate, IDependency
    {
        /// <summary>共享版本戳的键</summary>
        private const string StampKey = "ouroboros:config:version";

        /// <summary>指纹串里的字段分隔符：用不可见控制字符，正常配置文本里不会出现</summary>
        private const char FieldSeparator = '\u001f';

        /// <summary>
        /// 最多每 5 秒查一次共享戳。一次 GET 对 Redis 是噪声级别（单实例上限 12 次/分钟），
        /// 而人手点完 refresh 再等 5 秒完全无感 —— 相比它替换掉的 60 秒 TTL 快了一个数量级。
        /// </summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 最多每 3 秒比一次库侧指纹。与"审批敏感"缓存同一档（3 秒），
        /// 于是"裸改库"到"新会话开始弹审批"的窗口从最长 60 秒收敛到 ~3 秒。
        /// 开销是每进程每 3 秒 4 条落在配置表上的投影查询，<b>与请求量无关</b>（节流命中时只有一次时间比较）。
        /// </summary>
        private static readonly TimeSpan FingerprintInterval = TimeSpan.FromSeconds(3);

        /// <summary>Redis 报错后的退避：否则每 5 秒就有一个请求卡在连接超时上</summary>
        private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(60);

        /// <summary>共享戳的存活期，比任何实例的检查间隔长得多即可；过期只会多清一次缓存</summary>
        private static readonly TimeSpan StampTtl = TimeSpan.FromDays(7);

        private readonly Lazy<IRedisService?> _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;
        private readonly object _lock = new();
        private long _generation;
        private string? _seenStamp;
        private DateTime _nextCheckAt = DateTime.MinValue;
        private string? _seenFingerprint;
        private DateTime _nextFingerprintAt = DateTime.MinValue;
        private int _notEnabledWarned;
        private int _fingerprintWarned;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="provider">用来**可选**解析 IRedisService：Redis 关闭时它根本没注册，构造注入会直接失败</param>
        /// <param name="scopeFactory">作用域工厂（读库比对指纹要现开作用域取 IMomoDbContext）</param>
        /// <param name="logger">日志</param>
        public ConfigVersionGate(IServiceProvider provider, IServiceScopeFactory scopeFactory, ILoggerContract logger)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            // Lazy + 单例：解析一次，且不会因为首次解析失败就把 null 固化成一辈子的结论
            _redis = new Lazy<IRedisService?>(() => provider.GetService(typeof(IRedisService)) as IRedisService);
        }

        /// <inheritdoc />
        /// <remarks>无锁读：取 Agent 是热路径，而这个值只在锁内自增</remarks>
        public long Generation => Interlocked.Read(ref _generation);

        /// <inheritdoc />
        public long EnsureFresh()
        {
            CheckSharedStamp();
            CheckFingerprint();

            return Generation;
        }

        /// <inheritdoc />
        public long Publish()
        {
            var stamp = Guid.NewGuid().ToString("N");
            var redis = _redis.Value;

            if (redis is null)
            {
                WarnNotEnabled();
                return BumpLocal();
            }

            try
            {
                redis.Add(StampKey, stamp, StampTtl);
            }
            catch (Exception ex)
            {
                _logger.Warning("写入配置版本戳失败，本次 refresh 只清本进程缓存：{0}", ex.Message);
                return BumpLocal();
            }

            lock (_lock)
            {
                _seenStamp = stamp;
                return ++_generation;
            }
        }

        /// <summary>
        /// 看一眼共享版本戳（Redis 可用时）。没有 Redis 是合法部署形态，只提醒一次，不刷日志。
        /// </summary>
        private void CheckSharedStamp()
        {
            var redis = _redis.Value;

            if (redis is null)
            {
                WarnNotEnabled();
                return;
            }

            lock (_lock)
            {
                if (DateTime.UtcNow < _nextCheckAt) return;
                _nextCheckAt = DateTime.UtcNow + CheckInterval;
            }

            string? stamp;
            try
            {
                stamp = redis.Get<string>(StampKey);
            }
            catch (Exception ex)
            {
                // Redis 只是配置传播的加速器：它挂了就退回只清本进程（库侧指纹仍在，裸改库照样能察觉），
                // 绝不让"取 Agent"这件事失败
                _logger.Warning("读取配置版本戳失败，本进程退回只清本地缓存：{0}", ex.Message);
                lock (_lock)
                {
                    _nextCheckAt = DateTime.UtcNow + FailureBackoff;
                    return;
                }
            }

            lock (_lock)
            {
                if (string.Equals(stamp, _seenStamp, StringComparison.Ordinal)) return;

                // 戳不同 = 别处改过配置（或戳刚被重建）→ 自增版本，让各缓存的记账对不上从而全清
                _seenStamp = stamp;
                _generation++;
            }
        }

        /// <summary>
        /// 比一次库侧指纹：变了就自增本进程版本，各缓存（Agent 装配、工具列表、配置值）随之整体失效。
        /// 首轮只记基线、不清缓存 —— 进程刚起来时缓存本来就是空的。
        ///
        /// 指纹比 <c>MAX(UpdatedAt) + COUNT(*)</c> 更硬：直接比"判定列的内容"，
        /// 于是不依赖 UpdatedAt 被维护（手工 UPDATE 忘了写它也算变过），增删行由行 Id 列表 + 行数覆盖。
        /// </summary>
        private void CheckFingerprint()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow < _nextFingerprintAt) return;
                _nextFingerprintAt = DateTime.UtcNow + FingerprintInterval;
            }

            string? fingerprint;
            try
            {
                fingerprint = ReadFingerprint();
            }
            catch (Exception ex)
            {
                // 库不可用就退避：否则每个请求都要在这里等一次连接超时（此时缓存本来也读不到新配置）
                lock (_lock) _nextFingerprintAt = DateTime.UtcNow + FailureBackoff;

                if (Interlocked.Exchange(ref _fingerprintWarned, 1) == 0)
                    _logger.Warning("读取配置指纹失败，裸改库的察觉暂停（{0} 秒后重试）：{1}",
                        FailureBackoff.TotalSeconds, ex.Message);
                return;
            }

            if (fingerprint is null) return;
            Interlocked.Exchange(ref _fingerprintWarned, 0);

            lock (_lock)
            {
                if (_seenFingerprint is null)
                {
                    _seenFingerprint = fingerprint;
                    return;
                }

                if (string.Equals(fingerprint, _seenFingerprint, StringComparison.Ordinal)) return;

                _seenFingerprint = fingerprint;
                _generation++;
            }
        }

        /// <summary>
        /// 读一次四张配置表的判定列拼成指纹串。列清单刻意只含"影响该不该审批 / 能不能用"的那些，
        /// 描述、Schema、Headers 这类大字段不进来（连接与请求头的变化由连接池自己的指纹负责）。
        /// </summary>
        private string? ReadFingerprint()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<IMomoDbContext>();
            if (db is null) return null;

            var sb = new StringBuilder(2048);
            AppendFingerprint(sb, "binding", db.FindList<FingerprintRow>(BindingFingerprintSql));
            AppendFingerprint(sb, "tool", db.FindList<FingerprintRow>(ToolFingerprintSql));
            AppendFingerprint(sb, "server", db.FindList<FingerprintRow>(ServerFingerprintSql));
            AppendFingerprint(sb, "agent", db.FindList<FingerprintRow>(AgentFingerprintSql));
            return sb.ToString();
        }

        /// <summary>把一张表的结果摊平进指纹串：先记标签与行数（增删行），再逐行记 Id 与判定列</summary>
        private static void AppendFingerprint(StringBuilder sb, string label, List<FingerprintRow> rows)
        {
            sb.Append(label).Append(FieldSeparator).Append(rows.Count).Append(FieldSeparator);

            foreach (var row in rows)
            {
                sb.Append(row.Id).Append(FieldSeparator)
                    .Append(row.AgentKey).Append(FieldSeparator)
                    .Append(row.CapabilityType).Append(FieldSeparator)
                    .Append(row.CapabilityKey).Append(FieldSeparator)
                    .Append(row.RequiresApproval).Append(FieldSeparator)
                    .Append(row.AllowedSubjectIds).Append(FieldSeparator)
                    .Append(row.IsEnabled).Append(FieldSeparator)
                    .Append(row.ToolKey).Append(FieldSeparator)
                    .Append(row.Transport).Append(FieldSeparator)
                    .Append(row.Endpoint).Append(FieldSeparator)
                    .Append(row.ServerName).Append(FieldSeparator)
                    .Append(row.ServerAddress).Append(FieldSeparator)
                    .Append(row.ApprovalMode).Append(FieldSeparator)
                    .Append(row.AllowedTools).Append(FieldSeparator)
                    .Append(row.AlwaysRequireToolNames).Append(FieldSeparator)
                    .Append(row.NeverRequireToolNames).Append(FieldSeparator)
                    .Append(row.ModelProfile).Append(FieldSeparator)
                    .Append(row.ActivePromptVersion).Append(FieldSeparator)
                    .Append(row.IsDeleted)
                    .Append('\n');
            }
        }

        /// <summary>自增本进程版本：本实例立刻生效，其余实例只能等 Redis 恢复</summary>
        private long BumpLocal()
        {
            lock (_lock)
            {
                _nextCheckAt = DateTime.UtcNow + CheckInterval;
                return ++_generation;
            }
        }

        /// <summary>没有 Redis 是合法部署形态，只提醒一次，不刷日志</summary>
        private void WarnNotEnabled()
        {
            if (Interlocked.Exchange(ref _notEnabledWarned, 1) == 0)
                _logger.Warning("未启用 Redis，配置变更只在本进程立即生效（跨实例靠库侧指纹与 TTL 收敛，键 {0}）", StampKey);
        }

        /// <summary>能力绑定表：审批覆盖、主体白名单、启用状态就是"该不该审批"的全部输入</summary>
        private const string BindingFingerprintSql =
            "SELECT Id, AgentKey, CapabilityType, CapabilityKey, RequiresApproval, AllowedSubjectIds, IsEnabled FROM OtCapabilityBinding ORDER BY Id";

        /// <summary>工具表：工具自身的审批要求与启用状态（绑定的覆盖优先级更高，但两者都要看得见）</summary>
        private const string ToolFingerprintSql =
            "SELECT Id, ToolKey, Transport, Endpoint, RequiresApproval, IsEnabled FROM OtTool ORDER BY Id";

        /// <summary>MCP 服务表：审批模式与两个名单、暴露白名单、启用状态</summary>
        private const string ServerFingerprintSql =
            "SELECT Id, ServerName, Transport, ServerAddress, ApprovalMode, AllowedTools, AlwaysRequireToolNames, NeverRequireToolNames, IsEnabled FROM OtMcpServer ORDER BY Id";

        /// <summary>Agent 表：启用/软删、档位与提示词版本（换档位、停用 Agent 也该立刻失效）</summary>
        private const string AgentFingerprintSql =
            "SELECT Id, AgentKey, ModelProfile, ActivePromptVersion, IsEnabled, IsDeleted FROM OtAgent ORDER BY Id";

        /// <summary>
        /// 指纹查询的投影行：四张表各取自己需要的列，共用一个 DTO（没取的列留默认值）。
        /// 列名与属性同名，Dapper 直接映射；枚举列按整数取 —— 指纹只做判等，不需要语义。
        /// </summary>
        private sealed class FingerprintRow
        {
            public long Id { get; set; }

            public string? AgentKey { get; set; }

            public string? CapabilityKey { get; set; }

            public string? ToolKey { get; set; }

            public string? ServerName { get; set; }

            public string? Endpoint { get; set; }

            public string? ServerAddress { get; set; }

            public string? AllowedSubjectIds { get; set; }

            public string? AllowedTools { get; set; }

            public string? AlwaysRequireToolNames { get; set; }

            public string? NeverRequireToolNames { get; set; }

            public string? ModelProfile { get; set; }

            public int? CapabilityType { get; set; }

            public int? Transport { get; set; }

            public int? ApprovalMode { get; set; }

            public int? ActivePromptVersion { get; set; }

            public bool? RequiresApproval { get; set; }

            public bool? IsEnabled { get; set; }

            public bool? IsDeleted { get; set; }
        }
    }
}
