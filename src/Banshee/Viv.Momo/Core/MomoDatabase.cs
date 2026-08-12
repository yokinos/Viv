using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
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

        protected EFAppContext? _writeDbContext;
        protected EFAppContext? _readDbContext;

        protected IDbTransaction? _transaction;
        protected int _timeOut = 30;
        protected static readonly HashSet<string> _primaryKeys = ["Id"];

        private readonly Lock _lock = new();
        // 串行化异步 BeginTransactionAsync 的 check+begin+set（Monitor 无法跨 await 持有，用信号量替代）
        private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
        private bool _disposed = false;

        public MomoDatabase(IVivContext vivContext, ILoggerContract logger)
        {
            ArgumentNullException.ThrowIfNull(vivContext);
            _vivContext = vivContext;
            TenantId = _vivContext.SubjectId;
            _logger = logger;

            SetOptions();
        }

        protected void SetOptions(DatabaseOptions? options = null)
        {
            var realOptions = options ?? VivConfigRegistry.Get<DatabaseOptions>();
            ArgumentNullException.ThrowIfNull(realOptions);
            _options = realOptions;
            _timeOut = realOptions.Timeout;
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
        /// 当前实例的TenantId
        /// </summary>
        public long TenantId { get; protected set; }

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
                    WriteLog($"BeginTransaction,{ex.Message}", ex);
                    return false;
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
                    WriteLog($"CommitTransaction,{ex.Message}", ex);
                    throw;
                }
                finally
                {
                    _transaction?.Dispose();
                    _transaction = null;
                }
            }
        }

        /// <summary>
        /// 回滚当前事务
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
            catch (Exception ex)
            {
                WriteLog($"BeginTransactionAsync,{ex.Message}", ex);
                return false;
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
            catch (Exception ex)
            {
                WriteLog($"CommitTransactionAsync,{ex.Message}", ex);
                throw;
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
        /// 异步回滚事务
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