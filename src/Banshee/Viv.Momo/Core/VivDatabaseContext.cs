using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Momo.Sync;
using Viv.Vva.Extension;
using Viv.Vva.Generic;

namespace Viv.Momo.Core
{
    /// <summary>
    /// Viv 框架下的数据库访问实现（基于 EFCore 与 Dapper，支持 PostgreSQL、SqlServer）
    /// </summary>
    public class VivDatabaseContext : VivDatabase, IVivDbContext
    {
        private bool _disposed;

        public VivDatabaseContext(IVivContext vivContext, IDistributedLogger logger)
            : base(vivContext, logger) { }

        #region Insert

        public bool Insert<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetValue(entity);
                var context = GetAppContext();
                context.Add(entity);
                var count = context.SaveChanges();
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Insert,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Insert<T>(IEnumerable<T> entities) where T : IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            try
            {
                AutoSetValue(entities.ToArray());
                var entityList = entities.ToList();
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = context.SaveChanges();
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _options.DatabaseSouce);
                    affected = context.DbConnection.Execute(tempSql, entityList, _transaction, _timeOut);
                }

                return affected > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Insert（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> InsertAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetValue(entity);
                var context = GetAppContext();
                context.Add(entity);
                var count = await context.SaveChangesAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"InsertAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> InsertAsync<T>(IEnumerable<T> entities) where T : IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            try
            {
                AutoSetValue(entities.ToArray());
                var entityList = entities.ToList();
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = await context.SaveChangesAsync();
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _options.DatabaseSouce);
                    affected = await context.DbConnection.ExecuteAsync(tempSql, entityList, _transaction, _timeOut);
                }

                return affected > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"InsertAsync（批量）,{ex.Message}", ex);
                return false;
            }
        }

        #endregion

        #region Update

        public bool Update<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();
                var existingEntity = context.Find(typeof(T), entity.Id);
                if (existingEntity != null)
                {
                    context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }

                var count = context.SaveChanges();
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Update,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Update<T>(IEnumerable<T> entities) where T : class, IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            var entityList = entities.Where(x => x.Id > 0).ToList();
            if (entityList.IsNullOrEmpty()) return false;

            try
            {
                var context = GetAppContext();
                int count;

                if (entityList.Count < EFMaxCount)
                {
                    count = EFBatchUpdate(entityList, context);
                }
                else
                {
                    count = DapperBatchUpdate(entityList, context);
                }

                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Update（批量）,{entityList.Count},{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> UpdateAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();
                var existingEntity = await context.FindAsync(typeof(T), entity.Id);
                if (existingEntity != null)
                {
                    context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }

                var count = await context.SaveChangesAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"UpdateAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> UpdateAsync<T>(IEnumerable<T> entities) where T : class, IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            var entityList = entities.Where(x => x.Id > 0).ToList();
            if (entityList.IsNullOrEmpty()) return false;

            try
            {
                var context = GetAppContext();
                int count;

                if (entityList.Count < EFMaxCount)
                {
                    count = await EFBatchUpdateAsync(entityList, context);
                }
                else
                {
                    count = await DapperBatchUpdateAsync(entityList, context);
                }

                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"UpdateAsync（批量）,{entityList.Count},{ex.Message}", ex);
                return false;
            }
        }

        private int EFBatchUpdate<T>(List<T> entities, EFAppContext context) where T : class, IEntity
        {
            var entityIds = entities.Select(e => e.Id).ToList();
            var existingEntities = context.Set<T>().Where(e => entityIds.Contains(e.Id)).ToList();

            foreach (var entity in entities)
            {
                var existing = existingEntities.FirstOrDefault(e => e.Id == entity.Id);
                if (existing != null)
                {
                    context.Entry(existing).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }
            }

            return context.SaveChanges();
        }

        private async Task<int> EFBatchUpdateAsync<T>(List<T> entities, EFAppContext context) where T : class, IEntity
        {
            var entityIds = entities.Select(e => e.Id).Distinct().ToList();
            var existingEntities = context.Set<T>().Where(e => entityIds.Contains(e.Id)).ToList();

            foreach (var entity in entities)
            {
                var existing = existingEntities.FirstOrDefault(e => e.Id == entity.Id);
                if (existing != null)
                {
                    context.Entry(existing).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }
            }

            return await context.SaveChangesAsync();
        }

        private int DapperBatchUpdate<T>(List<T> entities, EFAppContext context) where T : class, IEntity
        {
            var sqlList = BuildUpdateSqlList(entities);
            int count = 0;
            foreach (var item in sqlList)
            {
                if (string.IsNullOrEmpty(item.Key)) continue;
                count += context.DbConnection.Execute(item.Key, item.Value, _transaction, _timeOut);
            }
            return count;
        }

        private async Task<int> DapperBatchUpdateAsync<T>(List<T> entities, EFAppContext context) where T : class, IEntity
        {
            var sqlList = BuildUpdateSqlList(entities);
            int count = 0;
            foreach (var item in sqlList)
            {
                if (string.IsNullOrEmpty(item.Key)) continue;
                count += await context.DbConnection.ExecuteAsync(item.Key, item.Value, _transaction, _timeOut);
            }
            return count;
        }

        private List<KeyValueItem<string, DynamicParameters>> BuildUpdateSqlList<T>(List<T> entities, int pageSize = 200) where T : class, IEntity
        {
            var type = typeof(T);
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var result = new List<KeyValueItem<string, DynamicParameters>>();

            int totalPages = CalculateTotalPages(entities.Count, pageSize);
            for (int page = 1; page <= totalPages; page++)
            {
                var pageEntities = entities.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                if (pageEntities.Count == 0) continue;

                var sqlBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                sqlBuilder.Append($"UPDATE {tableName} SET ");

                var fieldSqls = new List<string>();
                foreach (var prop in properties)
                {
                    var propName = prop.Name;
                    if (_primaryKeys.Contains(propName, StringComparer.OrdinalIgnoreCase)) continue;

                    var dbField = SqlMagic.QuoteIdentifier(propName, _options.DatabaseSouce);
                    var idField = SqlMagic.QuoteIdentifier("Id", _options.DatabaseSouce);

                    var caseBuilder = new StringBuilder($"{dbField} = CASE {idField} ");
                    foreach (var entity in pageEntities)
                    {
                        var idValue = type.GetProperty("Id")?.GetValue(entity);
                        if (idValue == null) continue;

                        var paramValue = prop.GetValue(entity);
                        var paramName = $"@{propName}_{idValue}";
                        caseBuilder.Append($"WHEN {idValue} THEN {paramName} ");
                        parameters.Add(paramName, paramValue);
                    }
                    caseBuilder.Append($"ELSE {dbField} END");
                    fieldSqls.Add(caseBuilder.ToString());
                }

                sqlBuilder.Append(string.Join(", ", fieldSqls));

                var idParams = new List<string>();
                foreach (var entity in pageEntities)
                {
                    var idValue = type.GetProperty("Id")?.GetValue(entity);
                    if (idValue == null) continue;
                    var paramName = $"@Id_{idValue}";
                    idParams.Add(paramName);
                    parameters.Add(paramName, idValue);
                }

                sqlBuilder.Append($" WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSouce)} IN ({string.Join(", ", idParams)})");
                result.Add(new KeyValueItem<string, DynamicParameters>(sqlBuilder.ToString(), parameters));
            }

            return result;
        }

        #endregion

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
                    int affected = context.Set<T>()
                        .Where(x => ids.Contains(x.Id))
                        .ExecuteDelete();
                    return affected > 0;
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSouce)} IN @Ids";
                    int affected = context.DbConnection.Execute(deleteSql, new { Ids = ids }, _transaction, _timeOut);
                    return affected > 0;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Delete（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();
                var entry = context.Entry(entity);
                if (entry.State == EntityState.Detached)
                    context.Attach(entity);

                context.Remove(entity);
                var affected = await context.SaveChangesAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"DeleteAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(IEnumerable<T> entities) where T : class, IEntity
        {
            if (entities.IsNullOrEmpty()) return false;

            var ids = entities.Where(x => x.Id > 0).Select(x => x.Id).ToList();
            if (ids.IsNullOrEmpty()) return false;

            try
            {
                var context = GetAppContext();
                if (ids.Count < EFMaxCount)
                {
                    int affected = await context.Set<T>()
                        .Where(x => ids.Contains(x.Id))
                        .ExecuteDeleteAsync();
                    return affected > 0;
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSouce)} IN @Ids";
                    int affected = await context.DbConnection.ExecuteAsync(deleteSql, new { Ids = ids }, _transaction, _timeOut);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _options.DatabaseSouce);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _options.DatabaseSouce);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSouce);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSouce);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSouce);
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
                var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _options.DatabaseSouce);
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

        public async Task<T?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().SingleOrDefaultAsync(predicate).ConfigureAwait(false);
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
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _options.DatabaseSouce);
                return connection.QuerySingleOrDefault<T>(sql, new { Id = id }, null, _timeOut);
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
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSouce);

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _options.DatabaseSouce);
                return await connection.QueryFirstOrDefaultAsync<T>(sql, new { Id = id }, null, _timeOut).ConfigureAwait(false);
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

        public async Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().FirstOrDefaultAsync(predicate).ConfigureAwait(false);
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

        public async Task<List<T>> FindListAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().Where(predicate).ToListAsync().ConfigureAwait(false);
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
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _options.DatabaseSouce);
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
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _options.DatabaseSouce);
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

        #region ExecuteSql

        public bool ExecuteSql(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return false;

            try
            {
                var context = GetAppContext();
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"ExecuteSql（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ExecuteSqlAsync(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return false;

            try
            {
                var context = GetAppContext();
                var count = await context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut).ConfigureAwait(false);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"ExecuteSqlAsync（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public bool ExecuteSqlList(List<string> sqlList, object? parameters = null, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalPages = CalculateTotalPages(sqlList.Count, batchSize);
                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = sqlList.Skip((page - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batch) + ";";
                    connection.Execute(batchSql, parameters, transaction, _timeOut);
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    context.Database.CommitTransaction();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList);
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = null, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)await context.Database.BeginTransactionAsync();
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalPages = CalculateTotalPages(sqlList.Count, batchSize);
                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = sqlList.Skip((page - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batch) + ";";
                    await connection.ExecuteAsync(batchSql, parameters, transaction, _timeOut);
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList);
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public bool ExecuteSqlList(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var item in sqlList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        connection.Execute(item.Key, item.Value, transaction, _timeOut);
                    }
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    context.Database.CommitTransaction();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10).Select(x => x.Key))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList.Select(x => x.Key));
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)await context.Database.BeginTransactionAsync();
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var item in sqlList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        await connection.ExecuteAsync(item.Key, item.Value, transaction, _timeOut);
                    }
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10).Select(x => x.Key))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList.Select(x => x.Key));
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        #endregion

        #region Other

        public IVivDbContext? CreateContext(DatabaseOptions options)
        {
            if (options == null) return null;
            var dataContext = new VivDatabaseContext(_vivContext, _logger);
            dataContext.SetOptions(options);
            return dataContext;
        }

        public void ChangeTenant(long tenantId)
        {
            if (tenantId > 0)
                TenantId = tenantId;
        }

        public void IsAutoSetDefaultValue(bool flag)
        {
            IsAutoSetValue = flag;
        }

        public EFAppContext GetEFContext(DbReadWriteType readWriteType)
        {
            return GetAppContext(readWriteType);
        }

        public async Task SyncTableAsync(bool allowDrop = false, CancellationToken cancellationToken = default)
        {
            var context = GetAppContext(DbReadWriteType.Write);

            // 1. EF Core EnsureCreated：创建数据库中不存在的表
            await context.Database.EnsureCreatedAsync(cancellationToken);

            // 2. SchemaSynchronizer：处理列级变更
            var sync = new SchemaSynchronizer(_options);
            var entityTypes = sync.ScanEntityTypes();
            if (entityTypes.Count == 0)
                return;

            var expected = sync.BuildExpectedSchema(entityTypes);
            var actual = await sync.FetchActualSchemaAsync(cancellationToken);
            var diff = sync.Diff(expected, actual);

            if (!diff.HasChanges)
                return;

            // 默认禁止 DROP，避免改属性名时误删数据
            if (!allowDrop)
            {
                if (diff.DeletedTables.Count > 0)
                {
                    WriteLog($"SyncTable: skip DROP {diff.DeletedTables.Count} table(s): {string.Join(", ", diff.DeletedTables.Select(t => t.TableName))}", null!);
                    diff.DeletedTables.Clear();
                }
                foreach (var table in diff.ModifiedTables)
                {
                    var drops = table.ColumnDiffs.Where(c => c.Type == DiffType.Deleted).ToList();
                    if (drops.Count > 0)
                    {
                        WriteLog($"SyncTable: skip DROP {drops.Count} column(s) in [{table.TableName}]: {string.Join(", ", drops.Select(c => c.ColumnName))}", null!);
                        table.ColumnDiffs.RemoveAll(c => c.Type == DiffType.Deleted);
                    }
                }
                diff.ModifiedTables.RemoveAll(t => t.ColumnDiffs.Count == 0);
            }

            if (diff.HasChanges)
            {
                var ddl = sync.GenerateDdl(diff);
                foreach (var sql in ddl)
                    await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
        }

        #endregion

        #region Dispose

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {

            }

            base.Dispose(disposing);
            _disposed = true;
        }

        #endregion
    }
}