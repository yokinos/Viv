using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
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
        protected DatabaseOptions _options;
        protected readonly ILoggerContract _logger;
        protected readonly IDatabaseOptionsProvider _optionsProvider;
        protected EFAppContext? _writeDbContext;
        protected EFAppContext? _readDbContext;

        protected IDbTransaction? _transaction;
        protected int _timeOut = 30;
        protected static readonly HashSet<string> _primaryKeys = ["Id"];

        private readonly Lock _lock = new();
        // 串行化异步 BeginTransactionAsync 的 check+begin+set（Monitor 无法跨 await 持有，用信号量替代）
        private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
        private bool _disposed = false;

        public MomoDatabase(IVivContext vivContext, ILoggerContract logger, IDatabaseOptionsProvider optionsProvider)
        {
            ArgumentNullException.ThrowIfNull(vivContext);
            _vivContext = vivContext;
            _optionsProvider = optionsProvider;
            _logger = logger;
            SetOptions();
        }

        protected void SetOptions(DatabaseOptions? options = null)
        {
            var realOptions = options ?? VivConfigRegistry.Get<DatabaseOptions>();
            ArgumentNullException.ThrowIfNull(realOptions);
            // 只有"未显式指定"（默认配置路径）才走 provider 可插拔覆盖；
            // CreateContext 显式传入的 options 是调用方已选定的配置，不再被 provider 二次改
            _options = options == null ? _optionsProvider.GetOptions(realOptions) : realOptions;
            _timeOut = _options.Timeout;
        }

        /// <summary>
        /// 获取要使用的EFCore上下文（内部使用缓存，线程安全）
        /// </summary>
        [return: NotNull]
        public EFAppContext GetAppContext(DbReadWriteType dbReadWriteType = DbReadWriteType.Write)
        {
            return CreateEFAppContext(_options, dbReadWriteType);
        }

        /// <summary>
        /// 创建或获取缓存的EFAppContext
        /// </summary>
        public EFAppContext CreateEFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType, bool reload = false)
        {
            if (!options.IsReadWriteSplit)
            {
                dbReadWriteType = DbReadWriteType.Write;
            }

            lock (_lock)
            {
                if (dbReadWriteType == DbReadWriteType.Read)
                {
                    if (_readDbContext == null || reload)
                    {
                        var old = _readDbContext;
                        _readDbContext = new EFAppContext(options, DbReadWriteType.Read);
                        old?.Dispose();
                    }
                    return _readDbContext;
                }
                else
                {
                    if (_writeDbContext == null || reload)
                    {
                        var old = _writeDbContext;
                        _writeDbContext = new EFAppContext(options, DbReadWriteType.Write);
                        old?.Dispose();
                    }
                    return _writeDbContext;
                }
            }
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
        /// 自动设置默认值（Id、TenantId）
        /// </summary>
        protected void AutoSetValue<T>(params T[] entities) where T : IEntity
        {
            if (entities.IsNullOrEmpty() || !IsAutoSetValue) return;
            foreach (var entity in entities)
            {
                if (entity.Id == default)
                    entity.Id = IdMagic.NextId();

                if (entity is ITenant tenant)
                {
                    if (tenant.TenantId == default)
                        tenant.TenantId = TenantId;
                }
            }
        }

        /// <summary>
        /// 开启一个数据库事务（使用写库）
        /// </summary>
        public virtual bool BeginTransaction()
        {
            lock (_lock)
            {
                if (_transaction != null) return true;

                try
                {
                    var context = GetAppContext(DbReadWriteType.Write);
                    _transaction = (IDbTransaction)context.Database.BeginTransaction();
                    return true;
                }
                catch (Exception ex)
                {
                    throw WrapDatabaseException($"BeginTransaction,{ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 提交当前事务
        /// </summary>
        public virtual void CommitTransaction()
        {
            lock (_lock)
            {
                if (_transaction == null) return;

                try
                {
                    var context = GetAppContext(DbReadWriteType.Write);
                    context.Database.CommitTransaction();
                }
                catch (Exception ex)
                {
                    throw WrapDatabaseException($"CommitTransaction,{ex.Message}", ex);
                }
                finally
                {
                    _transaction?.Dispose();
                    _transaction = null;
                }
            }
        }

        /// <summary>
        /// 回滚当前事务。回滚失败只记日志不抛出，避免掩盖触发回滚的原始异常。
        /// </summary>
        public virtual void RollbackTransaction()
        {
            lock (_lock)
            {
                if (_transaction == null) return;

                try
                {
                    var context = GetAppContext(DbReadWriteType.Write);
                    context.Database.RollbackTransaction();
                }
                catch (Exception ex)
                {
                    WriteLog($"RollbackTransaction,{ex.Message}", ex);
                }
                finally
                {
                    _transaction?.Dispose();
                    _transaction = null;
                }
            }
        }

        /// <summary>
        /// 异步开启事务
        /// </summary>
        public virtual async Task<bool> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            // check + begin + set 全程串行：避免两个并发调用都通过 null 检查、各自开启事务的竞态
            await _transactionSemaphore.WaitAsync();
            try
            {
                lock (_lock)
                {
                    if (_transaction != null) return true;
                }

                var context = GetAppContext(DbReadWriteType.Write);
                var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                lock (_lock)
                {
                    _transaction = (IDbTransaction)transaction;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"BeginTransactionAsync,{ex.Message}", ex);
            }
            finally
            {
                _transactionSemaphore.Release();
            }
        }

        /// <summary>
        /// 异步提交事务
        /// </summary>
        public virtual async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            IDbTransaction? transactionToCommit;
            lock (_lock)
            {
                if (_transaction == null) return;
                transactionToCommit = _transaction;
            }

            try
            {
                var context = GetAppContext(DbReadWriteType.Write);
                await context.Database.CommitTransactionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"CommitTransactionAsync,{ex.Message}", ex);
            }
            finally
            {
                lock (_lock)
                {
                    transactionToCommit?.Dispose();
                    _transaction = null;
                }
            }
        }

        /// <summary>
        /// 异步回滚事务。回滚失败只记日志不抛出，避免掩盖触发回滚的原始异常。
        /// </summary>
        public virtual async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            IDbTransaction? transactionToRollback;
            lock (_lock)
            {
                if (_transaction == null) return;
                transactionToRollback = _transaction;
            }

            try
            {
                var context = GetAppContext(DbReadWriteType.Write);
                await context.Database.RollbackTransactionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                WriteLog($"RollbackTransactionAsync,{ex.Message}", ex);
            }
            finally
            {
                lock (_lock)
                {
                    transactionToRollback?.Dispose();
                    _transaction = null;
                }
            }
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
            var connType = _options.DatabaseSource == DatabaseSourceType.PostgreSQL
                ? VivConnType.PostgreSQL
                : VivConnType.SqlServer;
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
                _transactionSemaphore.Dispose();
            }

            _disposed = true;
        }
    }
}