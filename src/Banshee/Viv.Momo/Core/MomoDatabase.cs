using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Momo.Base;
using Viv.Momo.Dialect;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    public class MomoDatabase : IDisposable
    {
        protected readonly IVivContext _vivContext;
        protected readonly IVivContextAccessor _accessor;
        protected readonly ILoggerContract _logger;
        protected IDbTransaction? _transaction;

        protected DatabaseOptions _databaseOptions;
        protected EFAppContext? _writeDbContext;
        protected EFAppContext? _readDbContext;

        /// <summary>
        /// 本连接对应的 SQL 方言，构造时按 <see cref="DatabaseOptions.DatabaseSource"/> 定死。
        /// 所有 SQL 拼装（表名/列名/分页/字面量）都走它，调用方不再传 DatabaseSourceType。
        /// </summary>
        protected readonly ISqlDialect _dialect;

        /// <summary>
        /// 事务尝试观察者（可选，由宿主传入）。开启重试后 work 会被重放，
        /// 按尝试累积的外部状态（如本地事件队列）需要在失败尝试时回退 ——
        /// 由它实现，Momo 本身不认识那些状态。
        /// 没传（初始化工具、单元测试直建上下文）时不做任何回退。
        /// </summary>
        protected readonly ITransactionAttemptListener? _attemptListener;

        protected int _timeOut = 30;
        protected static readonly HashSet<string> _primaryKeys = ["Id"];

        /// <summary>
        /// 查询耗时与慢查询判据。EF 那条路随 EFAppContext 下发到 DbCommandInterceptor，
        /// Dapper 的执行点在子类 MomoDatabaseContext 里直接用。
        /// 与 _databaseOptions 一样在构造时定死，不随后续请求变化。
        /// </summary>
        protected QueryTelemetry _queryTelemetry = null!;

        private readonly Lock _lock = new();

        private bool _disposed = false;

        /// <summary>
        /// 用默认库配置构造。options 在构造时定死 —— 建过上下文之后再换配置，
        /// 缓存里的上下文和 <see cref="_databaseOptions"/> 会各说各话，故不再提供可变的 SetOptions。
        /// 需要指定另一份配置（如 <see cref="MomoDbContextFactory"/> 的一次性会话）时，
        /// 由调用方包一个 <see cref="IDatabaseOptionsProvider"/> 传进来。
        /// </summary>
        public MomoDatabase(IVivContext vivContext, ILoggerContract logger, IDatabaseOptionsProvider optionsProvider, ITransactionAttemptListener? attemptListener = null)
        {
            ArgumentNullException.ThrowIfNull(vivContext);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(optionsProvider);

            var options = optionsProvider.GetRealOptions();
            ArgumentNullException.ThrowIfNull(options);

            _vivContext = vivContext;
            _logger = logger;
            _attemptListener = attemptListener;
            _dialect = SqlMagic.For(options.DatabaseSource);
            _accessor = vivContext.VivContextAccessor
                ?? throw new InvalidOperationException("IVivContext.VivContextAccessor 不能为空：Momo 需要它挂租户过滤器。");
            _databaseOptions = options;
            _timeOut = options.Timeout;
            _queryTelemetry = new QueryTelemetry(logger, options.SlowQueryThresholdMs);
        }

        /// <inheritdoc />
        public ISqlDialect Dialect => _dialect;

        /// <summary>
        /// 获取本次会话要用的 EF 上下文。读 / 写各缓存一个、在实例内复用 —— 缓存是刻意的：
        /// Dapper 的 <c>_transaction</c> 来自这个上下文的连接，每次换新实例事务就对不上了。
        /// </summary>
        [return: NotNull]
        public EFAppContext GetAppContext(DbReadWriteType dbReadWriteType = DbReadWriteType.Write)
        {
            // 无读写分离 → 走主库
            if (!_databaseOptions.IsReadWriteSplit)
            {
                dbReadWriteType = DbReadWriteType.Write;
            }

            lock (_lock)
            {
                if (dbReadWriteType == DbReadWriteType.Read)
                {
                    return _readDbContext ??= CreateContext(DbReadWriteType.Read);
                }

                return _writeDbContext ??= CreateContext(DbReadWriteType.Write);
            }
        }

        /// <summary>
        /// 建上下文。租户访问器取自当前 <see cref="IVivContext"/>，不再有静态回落分支。
        /// </summary>
        private EFAppContext CreateContext(DbReadWriteType dbReadWriteType)
        {
            return new EFAppContext(_databaseOptions, _accessor, dbReadWriteType, _queryTelemetry);
        }

        /// <summary>
        /// 是否自动设置默认值
        /// </summary>
        public bool IsAutoSetValue { get; protected set; } = true;

        /// <summary>
        /// 当前租户。调用时从 <see cref="IVivContext"/> 读取，不在构造时缓存
        /// （Wolverine 先构造 DbContext 再 SetSnapshot，构造时冻结会让 Dapper 整条消息 TenantId=0）。
        /// <see cref="MomoDatabaseContext.ChangeTenant"/> 可覆盖本实例，不改请求上下文。
        /// </summary>
        public long TenantId
        {
            get => _tenantOverride ?? _vivContext.SubjectId;
            protected set => _tenantOverride = value;
        }

        private long? _tenantOverride;

        /// <summary>
        /// 当前登录用户，供 <see cref="ICreatedBy"/> / <see cref="IUpdatedBy"/> 盖章。
        ///
        /// 与 <see cref="TenantId"/> 同一读法：调用时从 <see cref="IVivContext"/> 读，不在构造时缓存
        /// （Wolverine 先构造 DbContext 再 SetSnapshot，构造时冻结会让整条消息读到 0）。
        ///
        /// 无登录上下文（Worker / 消息消费 / 后台任务，UserId == 0）返回 null —— 审计列是 long?，
        /// 记 0 会让「没有操作人」跟真实存在的 UserId = 0 混在一起。
        /// </summary>
        protected long? CurrentUserId
        {
            get
            {
                var userId = _vivContext.UserId;
                return userId == 0 ? null : userId;
            }
        }

        /// <summary>
        /// 获取当前写库的数据库连接（用于Dapper混合事务）
        /// </summary>
        public IDbConnection DbConnection
        {
            get
            {
                var context = GetAppContext(DbReadWriteType.Write);
                return context.DbConnection;
            }
        }

        /// <summary>
        /// 自动设置新增时的默认值：Id、TenantId + 审计四件套。
        ///
        /// 审计字段按能力逐个 opt-in（<c>entity is ICreatedAt</c> 运行时判断），泛型约束仍是
        /// <see cref="IEntity"/> —— 全仓四十来个实体只有一部分有四件套，收紧约束会让其余编译不过。
        ///
        /// 新增时四件套一起盖（创建与更新时间都取新增那一刻）：只盖创建的话，
        /// 「只插不改」的行更新时间会永远是 null。
        /// </summary>
        protected void AutoSetInsertValue<T>(params T[] entities) where T : IEntity
        {
            if (entities.IsNullOrEmpty() || !IsAutoSetValue) return;

            var now = DateTime.UtcNow;
            var userId = CurrentUserId;

            foreach (var entity in entities)
            {
                SetIdentityAndTenant(entity);

                if (entity is ICreatedAt createdAt) createdAt.CreatedAt = now;
                if (entity is ICreatedBy createdBy) createdBy.CreatedBy = userId;
                if (entity is IUpdatedAt updatedAt) updatedAt.UpdatedAt = now;
                if (entity is IUpdatedBy updatedBy) updatedBy.UpdatedBy = userId;
            }
        }

        /// <summary>
        /// 自动设置更新时的默认值：只盖 <see cref="IUpdatedAt"/> / <see cref="IUpdatedBy"/>。
        ///
        /// 更新路径绝不碰创建信息 —— 那是只写一次的。更新人/时间每次盖新的，且是无条件覆盖而不是
        /// 「为 default 才填」：调用方传进来的 UpdatedAt 通常就是 default，靠它判断会把时间永远写成空。
        ///
        /// 也不填 Id / TenantId —— 主键决定改哪一行，租户不允许被改（由 <see cref="CopyProtectedValues"/> 保护）。
        /// </summary>
        protected void AutoSetUpdateValue<T>(params T[] entities) where T : IEntity
        {
            if (entities.IsNullOrEmpty() || !IsAutoSetValue) return;

            var now = DateTime.UtcNow;
            var userId = CurrentUserId;

            foreach (var entity in entities)
            {
                if (entity is IUpdatedAt updatedAt) updatedAt.UpdatedAt = now;
                if (entity is IUpdatedBy updatedBy) updatedBy.UpdatedBy = userId;
            }
        }

        /// <summary>
        /// 把入参改不动的那几列从库里加载出来的那一份补回入参上，必须在
        /// <c>Entry(existing).CurrentValues.SetValues(entity)</c> 之前调用。
        ///
        /// SetValues 会把入参实体上的全部映射列无差别覆盖到被跟踪实体上，包括调用方根本不该改的列 ——
        /// 而入参上它们是 default，于是每次 Update 都把库里的值冲掉：
        /// 创建信息被冲成 NULL（每次更新都丢一次）；没填租户的入参把行搬到租户 0 去，
        /// 且因全局查询过滤器而在业务侧「消失」。
        ///
        /// 这两处在审计字段真正开始写入之前都是潜伏的（没人写过，冲掉了也看不出来）。
        /// </summary>
        protected static void CopyProtectedValues(IEntity from, IEntity to)
        {
            if (from is ITenant fromTenant && to is ITenant toTenant)
                toTenant.TenantId = fromTenant.TenantId;

            if (from is ICreatedAt fromCreatedAt && to is ICreatedAt toCreatedAt)
                toCreatedAt.CreatedAt = fromCreatedAt.CreatedAt;

            if (from is ICreatedBy fromCreatedBy && to is ICreatedBy toCreatedBy)
                toCreatedBy.CreatedBy = fromCreatedBy.CreatedBy;
        }

        private void SetIdentityAndTenant<T>(T entity) where T : IEntity
        {
            if (entity.Id == default)
                entity.Id = IdMagic.NextId();

            if (entity is ITenant tenant)
            {
                if (tenant.TenantId == default)
                    tenant.TenantId = TenantId;
            }
        }

        /// <summary>
        /// 取 EF 事务底层的 ADO 事务（Dapper、原生 <c>DbCommand</c> 要的是它）。
        ///
        /// EF Core 10 没有现成的 <c>GetDbTransaction()</c> 扩展方法，别去找。EF 的
        /// <see cref="IDbContextTransaction"/>（实际是 SqlServerTransaction）不是
        /// <see cref="IDbTransaction"/>，两者没有继承关系，直接强转必抛 InvalidCastException，
        /// 底层那个真事务只能经 <see cref="IInfrastructure{T}"/> 取出来。
        /// </summary>
        protected static IDbTransaction GetDbTransaction(IDbContextTransaction transaction)
        {
            if (transaction is IInfrastructure<DbTransaction> infrastructure)
            {
                return infrastructure.Instance;
            }

            throw new NotSupportedException($"事务类型 {transaction.GetType().FullName} 既不是 IDbTransaction，也取不到底层 DbTransaction，无法交给 Dapper 使用。");
        }

        /// <summary>
        /// 委托式事务：BEGIN / COMMIT 由执行策略包住，work 抛异常则整笔回滚。
        /// 已处于外层事务里时直接内联（不重开、不重试）。内层的 commitWhen 不参与判定，
        /// 提交与否只看最外层 —— 和工作单元的嵌套合并是同一条规则。
        ///
        /// <paramref name="cancellationToken"/> 只用于开事务和 work 里的命令。
        /// 提交和回滚是收尾，固定用 <see cref="CancellationToken.None"/>：
        /// 调用方令牌多半是请求中止或停机，取消提交会出现「调用方以为失败、库里已经落下」，
        /// 取消回滚则会留下没关掉的事务。
        /// </summary>
        public virtual Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
            => ExecuteInTransactionCoreAsync(work, commitWhen: null, cancellationToken);

        /// <summary>
        /// 同上，提交与否由 <paramref name="commitWhen"/> 判定（业务返回失败信封也算失败）。
        /// </summary>
        public virtual Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool> commitWhen, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commitWhen);
            return ExecuteInTransactionCoreAsync(work, commitWhen, cancellationToken);
        }

        private async Task<T> ExecuteInTransactionCoreAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool>? commitWhen, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(work);

            // 已在事务里：并入外层。这里绝不能新开一个，也不能单独重试 —— 边界由外层拥有。
            // commitWhen 也只在最外层生效；内层传了也不看，否则内层一次 false 会把外层已经决定要提交的结果改掉。
            if (_transaction is not null)
            {
                return await work(cancellationToken).ConfigureAwait(false);
            }

            var context = GetAppContext(DbReadWriteType.Write);
            var strategy = context.Database.CreateExecutionStrategy();

            // 开启重试后 work 会被整段重放：失败尝试里累积的外部状态（如本地事件队列）
            // 对应已回滚的写入，重试前必须让观察者撤掉，否则成功那次会把它们一起带出去。
            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    // 重试会重放 work。SaveChanges 成功之后实体会变成 Unchanged，并且原始值被收成新值；
                    // 事务随后回滚，库回到旧值，跟踪器却还以为新值已经落库。重放时 Update 的 SetValues
                    // 看到「当前值等于原始值」就不会再写。所以失败时按进入本次尝试前的快照整份还原，
                    // 尝试期间新出现的实体（含已经 SaveChanges 过的）直接摘掉。
                    var trackedBefore = SnapshotTracker(context);

                    _attemptListener?.OnAttemptStarting();

                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                    // Dapper 命令靠 _transaction 判断「我在事务里」，必须指向同一条连接上的 ADO 事务，
                    // 否则会出现「EF 在事务内、Dapper 在事务外」
                    lock (_lock)
                    {
                        _transaction = GetDbTransaction(transaction);
                    }

                    try
                    {
                        var result = await work(cancellationToken).ConfigureAwait(false);

                        if (commitWhen is null || commitWhen(result))
                        {
                            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        else
                        {
                            // 业务返回失败信封：库要回到尝试前，跟踪器和本地事件也要，否则后面的成功操作会把它们带出去
                            _attemptListener?.OnAttemptFailed();
                            RestoreTracker(context, trackedBefore);
                            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        }

                        return result;
                    }
                    catch (Exception ex)
                    {
                        // 本次尝试累积的外部状态先撤掉，再回滚 —— 顺序无所谓，但两件都得做
                        _attemptListener?.OnAttemptFailed();

                        // 回滚之后这笔写就没了，对应实体也不能留在跟踪器里等人下次 SaveChanges 顺手带出去
                        RestoreTracker(context, trackedBefore);

                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception rollbackEx)
                        {
                            // 回滚失败只记日志：此刻再抛会盖住真正触发回滚的那个异常
                            WriteLog($"ExecuteInTransactionAsync 回滚失败：{rollbackEx.Message}", rollbackEx);
                        }

                        // 执行策略靠「异常链里有没有 SqlException/PostgresException」判断是否重试，
                        // 而它剥壳只穿过 EF 自己造的异常 —— VivConnectionException 不是，
                        // 直接把包装抛出去，策略会认为"不可重试"，EnableRetryOnFailure 等于白挂。
                        // 所以这里把原始 DbException 剥出来抛给策略，最终失败时再由下面的 catch 包回去。
                        if (FindRetryableDbException(ex) is { } dbException)
                        {
                            ExceptionDispatchInfo.Capture(dbException).Throw();
                        }

                        throw;
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _transaction = null;
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch (DbException dbException)
            {
                // 重试耗尽（或本就不可重试）时保持对外契约：API 过滤器靠 VivConnectionException 映射 -5xx，
                // 指标收口也在 WrapDatabaseException 里
                throw WrapDatabaseException($"ExecuteInTransactionAsync 重试后仍失败,{dbException.Message}", dbException);
            }
        }

        /// <summary>
        /// 从异常链里找出第一个 <see cref="DbException"/>。
        ///
        /// 用途：把原始数据库异常交给 EF 的执行策略。`VivConnectionException` 是框架自己的包装，
        /// 而 EF 的剥壳（<c>ExecutionStrategy.CallOnWrappedException</c>）只穿过它自己创建的异常，
        /// 停在包装上就识别不出 `SqlException.IsTransient` / `PostgresException.SqlState`，永远不会重试。
        /// </summary>
        internal static DbException? FindRetryableDbException(Exception? exception)
        {
            while (exception is not null)
            {
                if (exception is DbException dbException)
                {
                    return dbException;
                }

                exception = exception.InnerException;
            }

            return null;
        }

        /// <summary>
        /// 进入一次事务尝试之前的跟踪器快照。实体按引用认，值是 Clone 出来的，
        /// 之后 SaveChanges 改原始值不会把这份基线一起改掉。
        /// </summary>
        internal static List<TrackerBaseline> SnapshotTracker(EFAppContext context)
        {
            var list = new List<TrackerBaseline>();
            foreach (var entry in context.ChangeTracker.Entries())
            {
                list.Add(new TrackerBaseline(
                    entry.Entity,
                    entry.State,
                    entry.OriginalValues.Clone(),
                    entry.CurrentValues.Clone()));
            }

            return list;
        }

        /// <summary>
        /// 把跟踪器收回进入本次尝试之前的样子。
        ///
        /// 尝试期间才出现的实体一律摘掉，不管现在是 Added 还是已经被 SaveChanges 收成 Unchanged。
        /// 尝试之前就在跟踪里的，当前值、原始值和状态都写回去 —— 不能 Clear()，
        /// 那会把事务外加载、事后还要 Update 的实体一起摘掉。
        /// </summary>
        internal static void RestoreTracker(EFAppContext context, List<TrackerBaseline> baseline)
        {
            var known = new Dictionary<object, TrackerBaseline>(ReferenceEqualityComparer.Instance);
            foreach (var item in baseline)
            {
                known[item.Entity] = item;
            }

            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (!known.TryGetValue(entry.Entity, out var saved))
                {
                    entry.State = EntityState.Detached;
                    continue;
                }

                entry.CurrentValues.SetValues(saved.Current);
                entry.OriginalValues.SetValues(saved.Original);
                entry.State = saved.State;
            }
        }

        /// <summary>一次尝试开始前，某个已跟踪实体的状态和两套属性值。</summary>
        internal readonly record struct TrackerBaseline(
            object Entity,
            EntityState State,
            PropertyValues Original,
            PropertyValues Current);

        /// <inheritdoc />
        public virtual Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            return ExecuteInTransactionAsync(async ct =>
            {
                await work(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        /// <summary>
        /// 是否处于活动事务中
        /// </summary>
        public bool IsInTransaction => _transaction != null;

        /// <summary>
        /// 当前写库上的 ADO 事务。没有外层事务时为 null。
        ///
        /// 认领、取消标记、幂等回查必须打主库，并且要把它交给 Dapper：
        /// 连接上已经挂着事务时，不传事务句柄，SQL Server 会直接拒绝执行。
        /// </summary>
        public IDbTransaction? AmbientTransaction => _transaction;

        /// <summary>
        /// 单次EF处理实体的最大数量（超过这个数量会用Dapper处理）
        /// </summary>
        protected const int EFMaxCount = 200;

        public static int CalculateTotalPages(int totalItems, int pageSize)
        {
            if (totalItems < 0 || pageSize <= 0)
                return 0;
            return (totalItems + pageSize - 1) / pageSize;
        }

        protected void WriteLog(string message, Exception ex)
        {
            _logger.Error(message, ex);
        }

        /// <summary>
        /// 数据库访问失败：记日志后包装为 <see cref="VivConnectionException"/> 抛出。
        /// 取消不包装，避免把 <see cref="OperationCanceledException"/> 吞成连接故障。
        /// </summary>
        protected VivConnectionException WrapDatabaseException(string message, Exception ex)
        {
            WriteLog(message, ex);
            var connType = _databaseOptions.DatabaseSource == DatabaseSourceType.PostgreSQL
                ? VivConnType.PostgreSQL
                : VivConnType.SqlServer;
            // 45 个调用点都从这儿过，失败计数只在这一处收口
            MomoMetrics.RecordError(connType);
            return new VivConnectionException(connType, message, ex);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                lock (_lock)
                {
                    _transaction?.Dispose();
                    _writeDbContext?.Dispose();
                    _readDbContext?.Dispose();

                    _transaction = null;
                    _writeDbContext = null;
                    _readDbContext = null;
                }
            }

            _disposed = true;
        }
    }
}
