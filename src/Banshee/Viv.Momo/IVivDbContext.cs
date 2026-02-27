using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Vva.Generic;

namespace Viv.Momo
{
    /// <summary>
    /// Viv 数据库访问定义
    /// </summary>
    public interface IVivDbContext : IDisposable
    {
        bool Insert<T>(T entity);
        bool Insert<T>(IEnumerable<T> entitys);
        Task<bool> InsertAsync<T>(T entity);
        Task<bool> InsertAsync<T>(IEnumerable<T> entity);
        bool Update<T>(T entity);
        bool Update<T>(IEnumerable<T> entitys);
        Task<bool> UpdateAsync<T>(T entity);
        Task<bool> UpdateAsync<T>(IEnumerable<T> entity);
        bool Delete<T>(T entity);
        bool Delete<T>(IEnumerable<T> entitys);
        Task<bool> DeleteAsync<T>(T entity);
        Task<bool> DeleteAsync<T>(IEnumerable<T> entity);

        Task<T> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate);
        Task<T> SingleOrDefaultAsync<T>(string sql, params object[] parameters);
        T SingleOrDefault<T>(Expression<Func<T, bool>> predicate);
        T SingleOrDefault<T>(string sql, params object[] parameters);

        T FirstOrDefault<T>(Expression<Func<T, bool>> predicate);
        T FirstOrDefault<T>(string sql, params object[] parameters);
        Task<T> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate);
        Task<T> FirstOrDefaultAsync<T>(string sql, params object[] parameters);
        IEnumerable<T> Fetch<T>(Expression<Func<T, bool>> predicate);
        IEnumerable<T> Fetch<T>(string sql, params object[] parameters);
        Task<IEnumerable<T>> FetchAsync<T>(Expression<Func<T, bool>> predicate);
        Task<IEnumerable<T>> FetchAsync<T>(string sql, params object[] parameters);

        PagedList<T> Page<T>(int pageIndex, int pageSize, string sql, params object[] parameters);
        Task<PagedList<T>> PageAsync<T>(int pageIndex, int pageSize, string sql, params object[] parameters);

        T GetValue<T>(string sql, params object[] parameters);
        Task<T> GetValueAsync<T>(string sql, params object[] parameters);

        bool ExecuteSql(string sql, params object[] parameters);
        bool ExecuteSqlList(string sql, params object[] parameters);
        Task<bool> ExecuteSqlAsync(string sql, params object[] parameters);
        Task<bool> ExecuteSqlListAsync(string sql, params object[] parameters);

        bool BeginTransaction();
        void CommitTransaction();
        void RollbackTransaction();
        Task<bool> BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();

        IVivDbContext CreateContext(DatabaseOptions options);
        void ChangeTenant(string tenantId);
        void ChangeVivAppId(string vivAppId);
        void CloseAutoSetValue();
        void EnableAutoSetValue();
    }
}
