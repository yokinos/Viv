using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Viv.Delusion.Extension;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
        #region Delete

        public bool Delete<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();
                var entry = context.Entry(entity);
                if (entry.State == EntityState.Detached)
                    context.Attach(entity);

                context.Remove(entity);
                var affected = context.SaveChanges();
                return affected > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Delete,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Delete<T>(IEnumerable<T> entities) where T : class, IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            var ids = entities.Where(x => x.Id > 0).Select(x => x.Id).ToList();
            if (ids.IsNullOrEmpty()) return false;

            try
            {
                var context = GetAppContext();
                if (ids.Count < EFMaxCount)
                {
                    int affected = context.Set<T>().Where(x => ids.Contains(x.Id)).ExecuteDelete();
                    return affected > 0;
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                    var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T)) && TenantId > 0;
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSource)} IN @Ids"
                        + (isTenantEntity ? $" AND {SqlMagic.QuoteIdentifier("TenantId", _options.DatabaseSource)} = @TenantId" : "");
                    int affected = context.DbConnection.Execute(deleteSql, isTenantEntity ? new { Ids = ids, TenantId } : new { Ids = ids }, _transaction, _timeOut);
                    return affected > 0;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Delete（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();
                var entry = context.Entry(entity);
                if (entry.State == EntityState.Detached)
                    context.Attach(entity);

                context.Remove(entity);
                var affected = await context.SaveChangesAsync(cancellationToken);
                return affected > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"DeleteAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            var ids = entities.Where(x => x.Id > 0).Select(x => x.Id).ToList();
            if (ids.IsNullOrEmpty()) return false;

            try
            {
                var context = GetAppContext();
                if (ids.Count < EFMaxCount)
                {
                    int affected = await context.Set<T>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
                    return affected > 0;
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                    var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T)) && TenantId > 0;
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSource)} IN @Ids" 
                        + (isTenantEntity ? $" AND {SqlMagic.QuoteIdentifier("TenantId", _options.DatabaseSource)} = @TenantId" : "");
                    int affected = await context.DbConnection.ExecuteAsync(deleteSql, isTenantEntity ? new { Ids = ids, TenantId } : new { Ids = ids }, _transaction, _timeOut);
                    return affected > 0;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"DeleteAsync（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public bool Delete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Delete（委托）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var count = await context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"DeleteAsync（委托）,{ex.Message}", ex);
                return false;
            }
        }

        #endregion

        #region SoftDelete

        public bool SoftDelete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDelete
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext(DbReadWriteType.Write);
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"SoftDelete（委托）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> SoftDeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDelete
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var count = await context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"SoftDeleteAsync（委托）,{ex.Message}", ex);
                return false;
            }
        }

        public bool SoftDelete<T>(long id) where T : class, IEntity, ISoftDelete
        {
            if (id <= 0) return false;

            try
            {
                Expression<Func<T, bool>> predicate = x => x.Id == id;
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                var context = GetAppContext(DbReadWriteType.Write);
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"SoftDelete（Id）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> SoftDeleteAsync<T>(long id) where T : class, IEntity, ISoftDelete
        {
            if (id <= 0) return false;

            try
            {
                Expression<Func<T, bool>> predicate = x => x.Id == id;
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSource, TenantId);
                var context = GetAppContext(DbReadWriteType.Write);
                var count = await context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"SoftDeleteAsync（Id）,{ex.Message}", ex);
                return false;
            }
        }

        #endregion
    }
}
