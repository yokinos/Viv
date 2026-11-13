using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Momo.Core;
using Viv.Momo.Dialect;
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
        ISqlDialect Dialect { get; }
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
        Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool> commitWhen, CancellationToken cancellationToken = default);

        /// <summary>
        /// 当前是否处于活动事务中（即正跑在 <see cref="ExecuteInTransactionAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/> 的委托里）。
        ///
        /// 事务状态挂在<b>实例</b>上，所以这个判据只对「与业务同一作用域」拿到的那个实例成立 ——
        /// 也正是它能验证的东西：入队和业务写是不是同一个事务。它的用途是让「以为原子其实不原子」
        /// 的调用点在开发期就炸，而不是等线上数据对不上再回头查。
        /// </summary>
        bool IsInTransaction { get; }
        void ChangeTenant(long tenantId);
        void IsAutoSetDefaultValue(bool flag);
        EFAppContext GetEFContext(DbReadWriteType readWriteType = DbReadWriteType.Read);
        IDbConnection GetDbConnection(DbReadWriteType readWriteType = DbReadWriteType.Read);
        Task SyncTableAsync(bool allowDrop = false, bool allowAlterColumn = false, CancellationToken cancellationToken = default);
    }
}
