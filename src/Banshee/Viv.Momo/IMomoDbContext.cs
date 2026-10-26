using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo
{
    /// <summary>
    /// Viv 数据库访问定义。访问失败抛 <see cref="Viv.Contracts.Exceptions.VivConnectionException"/>；
    /// 写操作返回 false 仅表示影响 0 行或入参无效，不表示数据库故障。
    /// </summary>
    public interface IMomoDbContext : IDisposable
    {
        bool Insert<T>(T entity) where T : IEntity;
        bool Insert<T>(IEnumerable<T> entitys) where T : IEntity;
        Task<bool> InsertAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity;
        Task<bool> InsertAsync<T>(IEnumerable<T> entity, CancellationToken cancellationToken = default) where T : IEntity;
        bool Update<T>(T entity) where T : IEntity;
        bool Update<T>(IEnumerable<T> entitys) where T : class, IEntity;
        Task<bool> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity;
        Task<bool> UpdateAsync<T>(IEnumerable<T> entitys, CancellationToken cancellationToken = default) where T : class, IEntity;
        bool Delete<T>(T entity) where T : IEntity;
        bool Delete<T>(IEnumerable<T> entitys) where T : class, IEntity;
        Task<bool> DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity;
        Task<bool> DeleteAsync<T>(IEnumerable<T> entity, CancellationToken cancellationToken = default) where T : class, IEntity;
        bool Delete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<bool> DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity;
        bool Delete<T>(long id) where T : class, IEntity;
        Task<bool> DeleteAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity;
        bool SoftDelete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDeleted;
        Task<bool> SoftDeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity, ISoftDeleted;
        bool SoftDelete<T>(long id) where T : class, IEntity, ISoftDeleted;
        Task<bool> SoftDeleteAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity, ISoftDeleted;
        bool Exist<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<bool> ExistAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity;
        int Count<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<int> CountAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity;
        T? SingleOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class;
        T? SingleOrDefault<T>(string sql, object? parameters = default) where T : class;
        Task<T?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class;
        Task<T?> SingleOrDefaultAsync<T>(string sql, object? parameters = default, CancellationToken cancellationToken = default) where T : class;
        T? Find<T>(long id) where T : class, IEntity;
        Task<T?> FindAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity;
        T? FirstOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class;
        T? FirstOrDefault<T>(string sql, object? parameters = default) where T : class;
        Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class;
        Task<T?> FirstOrDefaultAsync<T>(string sql, object? parameters = default, CancellationToken cancellationToken = default) where T : class;
        List<T> FindList<T>(Expression<Func<T, bool>> predicate) where T : class;
        List<T> FindList<T>(string sql, object? parameters = default) where T : class;
        Task<List<T>> FindListAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class;
        Task<List<T>> FindListAsync<T>(string sql, object? parameters = default, CancellationToken cancellationToken = default) where T : class;
        T? FindScalar<T>(string sql, object? parameters = default);
        Task<T?> FindScalarAsync<T>(string sql, object? parameters = default, CancellationToken cancellationToken = default);
        PagedList<T> Page<T>(string sql, int pageIndex, int pageSize, object? parameters = default);
        Task<PagedList<T>> PageAsync<T>(string sql, int pageIndex, int pageSize, object? parameters = default, CancellationToken cancellationToken = default);
        bool ExecuteSql(string sql, object? parameters = default);
        Task<bool> ExecuteSqlAsync(string sql, object? parameters = default, CancellationToken cancellationToken = default);
        bool ExecuteSqlList(List<string> sqlList, object? parameters = default, bool isTxn = true);
        Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = default, bool isTxn = true, CancellationToken cancellationToken = default);
        bool ExecuteSqlList(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true);
        Task<bool> ExecuteSqlListAsync(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true, CancellationToken cancellationToken = default);
        /// <summary>
        /// 委托式事务：BEGIN / COMMIT 都在执行策略内部，work 抛异常则整笔回滚。
        /// 已处于外层事务中时直接内联执行（不重开、不重试）。
        /// 注意：开启重试后 work 可能被整段重放，副作用只能落在数据库上（内存队列里的本地事件需要按尝试重置）。
        /// </summary>
        Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

        /// <inheritdoc cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>
        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

        /// <summary>
        /// 同上，但由 <paramref name="commitWhen"/> 决定这笔事务提交还是回滚 ——
        /// 用于「业务返回失败信封也要回滚」（HTTP 仍 200）这类场景：判据拿到返回值，返回 false 则回滚。
        /// 无论提交还是回滚，work 的返回值都会原样返回。
        /// </summary>
        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool> commitWhen, CancellationToken cancellationToken = default);
        void ChangeTenant(long tenantId);
        void IsAutoSetDefaultValue(bool flag);
        EFAppContext GetEFContext(DbReadWriteType readWriteType = DbReadWriteType.Read);
        IDbConnection GetDbConnection(DbReadWriteType readWriteType = DbReadWriteType.Read);
        Task SyncTableAsync(bool allowDrop = false, bool allowAlterColumn = false, CancellationToken cancellationToken = default);
    }
}
