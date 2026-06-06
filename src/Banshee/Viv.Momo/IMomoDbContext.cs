using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Text;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Vva.Generic;

namespace Viv.Momo
{
    /// <summary>
    /// Viv 数据库访问定义
    /// </summary>
    public interface IMomoDbContext : IDisposable
    {
        bool Insert<T>(T entity) where T : IEntity;
        bool Insert<T>(IEnumerable<T> entitys) where T : IEntity;
        Task<bool> InsertAsync<T>(T entity) where T : IEntity;
        Task<bool> InsertAsync<T>(IEnumerable<T> entity) where T : IEntity;
        bool Update<T>(T entity) where T : IEntity;
        bool Update<T>(IEnumerable<T> entitys) where T : class, IEntity;
        Task<bool> UpdateAsync<T>(T entity) where T : IEntity;
        Task<bool> UpdateAsync<T>(IEnumerable<T> entitys) where T : class, IEntity;
        bool Delete<T>(T entity) where T : IEntity;
        bool Delete<T>(IEnumerable<T> entitys) where T : class, IEntity;
        Task<bool> DeleteAsync<T>(T entity) where T : IEntity;
        Task<bool> DeleteAsync<T>(IEnumerable<T> entity) where T : class, IEntity;
        bool Delete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<bool> DeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        bool SoftDelete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDelete;
        Task<bool> SoftDeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDelete;
        bool SoftDelete<T>(long id) where T : class, IEntity, ISoftDelete;
        Task<bool> SoftDeleteAsync<T>(long id) where T : class, IEntity, ISoftDelete;
        bool Exist<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<bool> ExistAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        int Count<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        Task<int> CountAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity;
        T? SingleOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class;
        T? SingleOrDefault<T>(string sql, object? parameters = default) where T : class;
        Task<T?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class;
        Task<T?> SingleOrDefaultAsync<T>(string sql, object? parameters = default) where T : class;
        T? Find<T>(long id) where T : class, IEntity;
        Task<T?> FindAsync<T>(long id) where T : class, IEntity;
        T? FirstOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class;
        T? FirstOrDefault<T>(string sql, object? parameters = default) where T : class;
        Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class;
        Task<T?> FirstOrDefaultAsync<T>(string sql, object? parameters = default) where T : class;
        List<T> FindList<T>(Expression<Func<T, bool>> predicate) where T : class;
        List<T> FindList<T>(string sql, object? parameters = default) where T : class;
        Task<List<T>> FindListAsync<T>(Expression<Func<T, bool>> predicate) where T : class;
        Task<List<T>> FindListAsync<T>(string sql, object? parameters = default) where T : class;
        T? FindScalar<T>(string sql, object? parameters = default);
        Task<T?> FindScalarAsync<T>(string sql, object? parameters = default);
        PagedList<T> Page<T>(string sql, int pageIndex, int pageSize, object? parameters = default);
        Task<PagedList<T>> PageAsync<T>(string sql, int pageIndex, int pageSize, object? parameters = default);
        bool ExecuteSql(string sql, object? parameters = default);
        Task<bool> ExecuteSqlAsync(string sql, object? parameters = default);
        bool ExecuteSqlList(List<string> sqlList, object? parameters = default, bool isTxn = true);
        Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = default, bool isTxn = true);
        bool ExecuteSqlList(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true);
        Task<bool> ExecuteSqlListAsync(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true);
        bool BeginTransaction();
        void CommitTransaction();
        void RollbackTransaction();
        Task<bool> BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
        IMomoDbContext? CreateContext(DatabaseOptions options);
        void ChangeTenant(long tenantId);
        void IsAutoSetDefaultValue(bool flag);
        EFAppContext GetEFContext(DbReadWriteType readWriteType = DbReadWriteType.Read);
        IDbConnection GetDbConnection(DbReadWriteType readWriteType = DbReadWriteType.Read);
        Task SyncTableAsync(bool allowDrop = false, CancellationToken cancellationToken = default);
    }
}
