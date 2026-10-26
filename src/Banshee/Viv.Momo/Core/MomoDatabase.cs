using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Momo.Base;
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
            _accessor = vivContext.VivContextAccessor
                ?? throw new InvalidOperationException("IVivContext.VivContextAccessor 不能为空：Momo 需要它挂租户过滤器。");
            _databaseOptions = options;
            _timeOut = options.Timeout;
            _queryTelemetry = new QueryTelemetry(logger, options.SlowQueryThresholdMs);
        }

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
        /// 已处于外层事务里时直接内联（不重开、不重试）。
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

            // 已在事务里：并入外层。这里绝不能新开一个，也不能单独重试 —— 边界由外层拥有
            if (_transaction is not null)
            {
                return await work(cancellationToken).ConfigureAwait(false);
            }

            var context = GetAppContext(DbReadWriteType.Write);
            var strategy = context.Database.CreateExecutionStrategy();

            // 开启重试后 work 会被整段重放：失败尝试里累积的外部状态（如本地事件队列）
            // 对应已回滚的写入，重试前必须让观察者撤掉，否则成功那次会把它们一起带出去。
            return await strategy.ExecuteAsync(async () =>
            {
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
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return result;
                }
                catch
                {
                    // 本次尝试累积的外部状态先撤掉，再回滚 —— 顺序无所谓，但两件都得做
                    _attemptListener?.OnAttemptFailed();

                    try
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception rollbackEx)
                    {
                        // 回滚失败只记日志：此刻再抛会盖住真正触发回滚的那个异常
                        WriteLog($"ExecuteInTransactionAsync 回滚失败：{rollbackEx.Message}", rollbackEx);
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
