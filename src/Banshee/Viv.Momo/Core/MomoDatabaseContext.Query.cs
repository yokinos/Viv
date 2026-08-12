using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Viv.Delusion.Generic;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
        #region Query (Exist, Count, Single, First, Find, List)

        public bool Exist<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().Any(predicate);
            }
            catch (Exception ex)
            {
                WriteLog($"Exist（委托）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ExistAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().AnyAsync(predicate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"ExistAsync（委托）,{ex.Message}", ex);
                return false;
            }
        }

        public int Count<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return -1;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().Count(predicate);
            }
            catch (Exception ex)
            {
                WriteLog($"Count（委托）,{ex.Message}", ex);
                return -1;
            }
        }

        public async Task<int> CountAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return -1;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().CountAsync(predicate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"CountAsync（委托）,{ex.Message}", ex);
                return -1;
            }
        }

        public T? SingleOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().SingleOrDefault(predicate);
            }
            catch (InvalidOperationException ex)
            {
                WriteLog($"SingleOrDefault（委托）: 实体{typeof(T).Name}符合条件的记录超过1条，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                WriteLog($"SingleOrDefault（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public T? SingleOrDefault<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                return connection.QuerySingleOrDefault<T>(sql, parameters, null, _timeOut);
            }
            catch (InvalidOperationException ex)
            {
                WriteLog($"SingleOrDefault（SQL）: 实体{typeof(T).Name}符合条件的记录超过1条，SQL：{sql}，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                WriteLog($"SingleOrDefault（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().SingleOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                WriteLog($"SingleOrDefaultAsync（委托）: 实体{typeof(T).Name}符合条件的记录超过1条，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                WriteLog($"SingleOrDefaultAsync（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> SingleOrDefaultAsync<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters, null, _timeOut).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                WriteLog($"SingleOrDefaultAsync（SQL）: 实体{typeof(T).Name}符合条件的记录超过1条，SQL：{sql}，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                WriteLog($"SingleOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        [return: MaybeNull]
        public T? Find<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);

            try
            {
                var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T));
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _options.DatabaseSource, isTenantEntity);
                // ITenant 实体按租户过滤，防止跨租户按 Id 读取
                object parameters = isTenantEntity ? new { Id = id, TenantId } : new { Id = id };
                // 与 FindAsync 的 QueryFirstOrDefaultAsync 保持一致：重复行取首行，不抛（宽松语义）
                return connection.QueryFirstOrDefault<T>(sql, parameters, null, _timeOut);
            }
            catch (Exception ex)
            {
                WriteLog($"Find,Table:{tableName},Id:{id},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FindAsync<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);

            try
            {
                var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T));
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _options.DatabaseSource, isTenantEntity);
                object parameters = isTenantEntity ? new { Id = id, TenantId } : new { Id = id };
                return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters, null, _timeOut).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"FindAsync,Table:{tableName},Id:{id},{ex.Message}", ex);
                return default;
            }
        }

        public T? FirstOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().FirstOrDefault(predicate);
            }
            catch (Exception ex)
            {
                WriteLog($"FirstOrDefault（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public T? FirstOrDefault<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                return connection.QueryFirstOrDefault<T>(sql, parameters, null, _timeOut);
            }
            catch (Exception ex)
            {
                WriteLog($"FirstOrDefault（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"FirstOrDefaultAsync（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FirstOrDefaultAsync<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters, null, _timeOut).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"FirstOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        [return: NotNull]
        public List<T> FindList<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().Where(predicate).ToList();
            }
            catch (Exception ex)
            {
                WriteLog($"FindList（委托）,{ex.Message}", ex);
                return [];
            }
        }

        public List<T> FindList<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var result = context.DbConnection.Query<T>(sql, parameters, null, true, _timeOut);
                return result.ToList();
            }
            catch (Exception ex)
            {
                WriteLog($"FindList（SQL）,{sql},{ex.Message}", ex);
                return [];
            }
        }

        public async Task<List<T>> FindListAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class
        {
            if (predicate == null) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"FindListAsync（委托）,{ex.Message}", ex);
                return [];
            }
        }

        public async Task<List<T>> FindListAsync<T>(string sql, object? parameters = null) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var result = await context.DbConnection.QueryAsync<T>(sql, parameters, null, _timeOut).ConfigureAwait(false);
                return result.ToList();
            }
            catch (Exception ex)
            {
                WriteLog($"FindListAsync（SQL）,{sql},{ex.Message}", ex);
                return [];
            }
        }

        public T? FindScalar<T>(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.DbConnection.QueryFirstOrDefault<T>(sql, parameters, null, _timeOut);
            }
            catch (Exception ex)
            {
                WriteLog($"FindScalar（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FindScalarAsync<T>(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var result = await context.DbConnection.QueryFirstOrDefaultAsync<T>(sql, parameters, null, _timeOut).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                WriteLog($"FindScalarAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        #endregion

        #region PagedList

        public PagedList<T> Page<T>(string sql, int pageIndex, int pageSize, object? parameters = null)
        {
            var result = new PagedList<T>(pageIndex, pageSize);
            if (string.IsNullOrEmpty(sql)) return result;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _options.DatabaseSource);
                var totalCount = context.DbConnection.ExecuteScalar<int>(countSql, parameters, null, _timeOut);
                if (totalCount > 0)
                {
                    var totalPages = CalculateTotalPages(totalCount, pageSize);
                    var list = context.DbConnection.Query<T>(pageSql, parameters, null, true, _timeOut);
                    result.TotalCount = totalCount;
                    result.Items = list;
                    result.TotalPages = totalPages;
                    result.IsHaveFrontPage = pageIndex > 1;
                    result.IsHaveNextPage = pageIndex < totalPages;
                }
                return result;
            }
            catch (Exception ex)
            {
                WriteLog($"Page（SQL）,{sql},{ex.Message}", ex);
                return result;
            }
        }

        public async Task<PagedList<T>> PageAsync<T>(string sql, int pageIndex, int pageSize, object? parameters = null)
        {
            var result = new PagedList<T>(pageIndex, pageSize);
            if (string.IsNullOrEmpty(sql)) return result;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _options.DatabaseSource);
                var totalCount = await context.DbConnection.ExecuteScalarAsync<int>(countSql, parameters, null, _timeOut).ConfigureAwait(false);
                if (totalCount > 0)
                {
                    var totalPages = CalculateTotalPages(totalCount, pageSize);
                    var list = await context.DbConnection.QueryAsync<T>(pageSql, parameters, null, _timeOut).ConfigureAwait(false);
                    result.TotalCount = totalCount;
                    result.Items = list;
                    result.IsHaveFrontPage = pageIndex > 1;
                    result.TotalPages = totalPages;
                    result.IsHaveNextPage = pageIndex < totalPages;
                }
                return result;
            }
            catch (Exception ex)
            {
                WriteLog($"PageAsync（SQL）,{sql},{ex.Message}", ex);
                return result;
            }
        }

        #endregion
    }
}
