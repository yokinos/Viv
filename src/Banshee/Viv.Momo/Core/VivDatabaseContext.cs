using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Vva.Extension;
using Viv.Vva.Generic;

namespace Viv.Momo.Core
{
    /// <summary>
    /// Viv 框架下的数据库访问实现 （基于EFCore与Dapper）(支持PostgreSQL,SqlServer)
    /// </summary>
    public class VivDatabaseContext : VivDatabase, IVivDbContext
    {
        private bool _disposed = false;
        public VivDatabaseContext(IVivContext vivContext, IVivLogger vivLogger) : base(vivContext, vivLogger) { }

        public bool Insert<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetValue(entity);
                var _context = GetEFCoreContext();
                _context.Add(entity);
                var count = _context.SaveChanges();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Insert,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Insert<T>(IEnumerable<T> entitys) where T : IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            try
            {
                AutoSetValue(entitys);
                var count = entitys.Count();
                var context = GetEFCoreContext();
                int affected = 0;

                if (count < EFMaxCount)
                {
                    context.AddRange(entitys);
                    affected = context.SaveChanges();
                }
                else
                {
                    var genarater = GetSqlGenerater();
                    var tempsql = genarater.CreateInsertTemplateSql(XMagic.GetTableName<T>(), typeof(T));
                    affected = context.DbConnection.Execute(tempsql, entitys, _transaction, _timeOut);
                }

                return affected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Insert（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> InsertAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetValue(entity);
                var _context = GetEFCoreContext();
                _context.Add(entity);
                var count = await _context.SaveChangesAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"InsertAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> InsertAsync<T>(IEnumerable<T> entitys) where T : IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            try
            {
                AutoSetValue(entitys);
                var count = entitys.Count();
                var context = GetEFCoreContext();
                int affected = 0;

                if (count < EFMaxCount)
                {
                    context.AddRange(entitys);
                    affected = await context.SaveChangesAsync();
                }
                else
                {
                    var genarater = GetSqlGenerater();
                    var tempsql = genarater.CreateInsertTemplateSql(XMagic.GetTableName<T>(), typeof(T));
                    affected = await context.DbConnection.ExecuteAsync(tempsql, entitys, _transaction, _timeOut);
                }

                return affected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"InsertAsync（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public bool Update<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var _context = GetEFCoreContext();
                var existingEntity = _context.Find(typeof(T), entity.Id);
                if (existingEntity != null)
                {
                    _context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    _context.Update(entity);
                }

                var count = _context.SaveChanges();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Update,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Update<T>(IEnumerable<T> entitys) where T : class, IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            var entityList = entitys.Where(x => x.Id > 0).ToList();
            if (entityList.IsNullOrEmpty())
            {
                return false;
            }

            try
            {
                var _context = GetEFCoreContext();
                var count = 0;
                if (entityList.Count < EFMaxCount)
                {
                    count = EFBatchUpdate(entityList, _context);
                }
                else
                {
                    count = DapperBatchUpdate(entityList, _context);
                }

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Update（批量）,{entityList.Count},{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> UpdateAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var _context = GetEFCoreContext();
                var existingEntity = _context.Find(typeof(T), entity.Id);
                if (existingEntity != null)
                {
                    _context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    _context.Update(entity);
                }

                var count = await _context.SaveChangesAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"UpdateAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> UpdateAsync<T>(IEnumerable<T> entitys) where T : class, IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            var entityList = entitys.Where(x => x.Id > 0).ToList();
            if (entityList.IsNullOrEmpty())
            {
                return false;
            }

            try
            {
                var _context = GetEFCoreContext();
                var count = 0;
                if (entityList.Count < EFMaxCount)
                {
                    count = await EFBatchUpdateAsync(entityList, _context);
                }
                else
                {
                    count = await DapperBatchUpdateAsync(entityList, _context);
                }

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"Update（批量）,{entityList.Count},{ex.Message}", ex);
                return false;
            }
        }

        private static int EFBatchUpdate<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
        {
            var entityIds = entitys.Select(e => e.Id).ToList();
            var existingEntities = context.Set<T>().Where(e => entityIds.Contains(e.Id)).ToList();

            foreach (var entity in entitys)
            {
                // 查找当前实体是否在已跟踪的列表中
                var existingEntity = existingEntities.FirstOrDefault(e => e.Id == entity.Id);
                if (existingEntity != null)
                {
                    context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }
            }

            int count = context.SaveChanges();
            return count;
        }

        private static async Task<int> EFBatchUpdateAsync<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
        {
            var entityIds = entitys.Select(e => e.Id).Distinct().ToList();
            var existingEntities = context.Set<T>().Where(e => entityIds.Contains(e.Id)).ToList();

            foreach (var entity in entitys)
            {
                // 查找当前实体是否在已跟踪的列表中
                var existingEntity = existingEntities.FirstOrDefault(e => e.Id == entity.Id);
                if (existingEntity != null)
                {
                    context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Update(entity);
                }
            }

            int count = await context.SaveChangesAsync();
            return count;
        }

        private int DapperBatchUpdate<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
        {
            var sqlList = BuilderUpdateSqlList(entitys);
            int count = 0;
            foreach (var item in sqlList)
            {
                if (item.Key.IsNullOrEmpty()) continue;
                count += context.DbConnection.Execute(item.Key, item.Value, _transaction, _timeOut);
            }

            return count;
        }

        private async Task<int> DapperBatchUpdateAsync<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
        {
            var sqlList = BuilderUpdateSqlList(entitys);
            int count = 0;
            foreach (var item in sqlList)
            {
                if (item.Key.IsNullOrEmpty()) continue;
                count += await context.DbConnection.ExecuteAsync(item.Key, item.Value, _transaction, _timeOut);
            }

            return count;
        }

        private List<KeyValueItem<string, DynamicParameters>> BuilderUpdateSqlList<T>(List<T> entityList, int pageSize = 200)
        {
            var type = typeof(T);
            var tableName = AdaptFieldNameToDatabase(XMagic.GetTableName<T>());
            var pilist = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sqlList = new List<KeyValueItem<string, DynamicParameters>>();

            var totalCount = entityList.Count;
            var totalPages = CalculateTotalPages(totalCount, pageSize);

            for (int index = 1; index <= totalPages; index++)
            {
                var currentPageList = entityList.Skip((index - 1) * pageSize).Take(pageSize).ToList();
                if (currentPageList.Count == 0) continue;

                var sqlBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                sqlBuilder.Append($"UPDATE {tableName} SET ");

                var fieldSqlList = new List<string>();
                foreach (var pi in pilist)
                {
                    var propertyName = pi.Name;
                    if (_primaryKeys.Contains(propertyName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dbField = AdaptFieldNameToDatabase(propertyName);
                    var idField = AdaptFieldNameToDatabase("Id");

                    var fieldCaseSql = new StringBuilder($"{dbField} = CASE {idField} ");
                    foreach (var entity in currentPageList)
                    {
                        var idValue = type.GetProperty("Id")?.GetValue(entity);
                        if (idValue == null) continue;
                        var propertyValue = pi.GetValue(entity);
                        var paramName = $"@{propertyName}_{idValue}";
                        fieldCaseSql.Append($"WHEN {idValue} THEN {paramName} ");
                        parameters.Add(paramName, propertyValue);
                    }

                    fieldCaseSql.Append($"ELSE {dbField} END");
                    fieldSqlList.Add(fieldCaseSql.ToString());
                }

                sqlBuilder.Append(string.Join(", ", fieldSqlList));

                var idParamList = new List<string>();
                foreach (var entity in currentPageList)
                {
                    var idValue = type.GetProperty("Id")?.GetValue(entity);
                    if (idValue == null) continue;
                    var idParamName = $"@Id_{idValue}";
                    idParamList.Add(idParamName);
                    parameters.Add(idParamName, idValue);
                }

                sqlBuilder.Append($" WHERE {AdaptFieldNameToDatabase("Id")} IN ({string.Join(", ", idParamList)})");
                sqlList.Add(new KeyValueItem<string, DynamicParameters>(sqlBuilder.ToString(), parameters));
            }

            return sqlList;
        }


        public bool Delete<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var _context = GetEFCoreContext();
                var entry = _context.Entry(entity);
                if (entry.State == EntityState.Detached)
                {
                    _context.Attach(entity);
                }

                _context.Remove(entity);
                var affectedCount = _context.SaveChanges();
                return affectedCount > 0;

            }
            catch (Exception ex)
            {
                _logger.Error($"Delete,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Delete<T>(IEnumerable<T> entitys) where T : class, IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            var ids = entitys.Where(x => x.Id > 0).Select(x => x.Id).ToList();
            if (ids.IsNullOrEmpty()) return false;

            try
            {
                var _context = GetEFCoreContext();
                if (ids.Count < EFMaxCount)
                {
                    int affectedCount = _context.Set<T>()
                        .Where(x => ids.Contains(x.Id))
                        .ExecuteDelete();

                    return affectedCount > 0;
                }
                else
                {
                    var connection = _context.DbConnection;
                    var deleteSql = $"DELETE FROM {XMagic.GetTableName<T>()} WHERE Id IN @Ids";
                    int affectedRows = connection.Execute(deleteSql, new { Ids = ids });
                    return affectedRows > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Delete（批量）,{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var _context = GetEFCoreContext();
                var entry = _context.Entry(entity);
                if (entry.State == EntityState.Detached)
                {
                    _context.Attach(entity);
                }

                _context.Remove(entity);
                var affectedCount = await _context.SaveChangesAsync();
                return affectedCount > 0;

            }
            catch (Exception ex)
            {
                _logger.Error($"DeleteAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteAsync<T>(IEnumerable<T> entitys) where T : class, IEntity
        {
            if (entitys.IsNullOrEmpty()) return false;

            var ids = entitys.Where(x => x.Id > 0).Select(x => x.Id).ToList();
            if (ids.IsNullOrEmpty()) return false;

            try
            {
                var _context = GetEFCoreContext();
                if (ids.Count < EFMaxCount)
                {
                    int affectedCount = await _context.Set<T>()
                        .Where(x => ids.Contains(x.Id))
                        .ExecuteDeleteAsync();

                    return affectedCount > 0;
                }
                else
                {
                    var connection = _context.DbConnection;
                    var deleteSql = $"DELETE FROM {XMagic.GetTableName<T>()} WHERE Id IN @Ids";
                    int affectedRows = await connection.ExecuteAsync(deleteSql, new { Ids = ids });
                    return affectedRows > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"DeleteAsync（批量）,{ex.Message}", ex);
                return false;
            }
        }

        [return: MaybeNull]
        public T? SingleOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                return _context.Set<T>().SingleOrDefault(predicate);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"SingleOrDefault（委托）: 实体{typeof(T).Name}符合条件的记录超过1条，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                _logger.Error($"SingleOrDefault（委托）,{ex.Message}", ex);
                return default;
            }
        }

        [return: MaybeNull]
        public T? SingleOrDefault<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                return connection.QuerySingleOrDefault<T>(sql, parameters, _transaction, _timeOut);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"SingleOrDefault（SQL）: 实体{typeof(T).Name}符合条件的记录超过1条，SQL：{sql}，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                _logger.Error($"SingleOrDefault（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                return await _context.Set<T>().SingleOrDefaultAsync(predicate).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"SingleOrDefaultAsync（委托）: 实体{typeof(T).Name}符合条件的记录超过1条，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                _logger.Error($"SingleOrDefaultAsync（委托）,{ex.Message}", ex);
                return default;
            }
        }

        [return: MaybeNull]
        public async Task<T?> SingleOrDefaultAsync<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters, _transaction, _timeOut).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"SingleOrDefaultAsync（SQL）: 实体{typeof(T).Name}符合条件的记录超过1条，SQL：{sql}，异常：{ex.Message}", ex);
                return default;
            }
            catch (Exception ex)
            {
                _logger.Error($"SingleOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        [return: MaybeNull]
        public T? Find<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = XMagic.GetTableName<T>();

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                var sql = GetSqlGenerater().GetFindSql(tableName);
                return connection.QuerySingleOrDefault<T>(sql, new { Id = id }, _transaction, _timeOut);
            }
            catch (Exception ex)
            {
                _logger.Error($"Find,Table:{tableName},Id:{id},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FindAsync<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = XMagic.GetTableName<T>();

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                var sql = GetSqlGenerater().GetFindSql(tableName);
                return await connection.QueryFirstOrDefaultAsync<T>(sql, new { Id = id }, _transaction, _timeOut).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"FindAsync,Table:{tableName},Id:{id},{ex.Message}", ex);
                return default;
            }
        }

        public T? FirstOrDefault<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                return _context.Set<T>().FirstOrDefault(predicate);
            }
            catch (Exception ex)
            {
                _logger.Error($"FirstOrDefault（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public T? FirstOrDefault<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                return connection.QueryFirstOrDefault<T>(sql, parameters, _transaction, _timeOut);
            }
            catch (Exception ex)
            {
                _logger.Error($"FirstOrDefault（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                return await _context.Set<T>().FirstOrDefaultAsync(predicate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"FirstOrDefaultAsync（委托）,{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FirstOrDefaultAsync<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var connection = _context.DbConnection;
                return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters, _transaction, _timeOut).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"FirstOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        [return: NotNull]
        public List<T> FindList<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return [];

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var result = _context.Set<T>().Where(predicate);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"FindList（委托）,{ex.Message}", ex);
                return [];
            }
        }

        public List<T> FindList<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return [];

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var result = _context.DbConnection.Query<T>(sql, parameters, _transaction, true, _timeOut);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"FindList（SQL）,{sql},{ex.Message}", ex);
                return [];
            }
        }

        public async Task<List<T>> FindListAsync<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            if (predicate == null) return [];

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                return await _context.Set<T>().Where(predicate).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"FindListAsync（委托）,{ex.Message}", ex);
                return [];
            }
        }

        public async Task<List<T>> FindListAsync<T>(string sql, object? parameters = default) where T : class
        {
            if (sql.IsNullOrEmpty()) return [];

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var result = await _context.DbConnection.QueryAsync<T>(sql, parameters, _transaction, _timeOut);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"FindListAsync（SQL）,{sql},{ex.Message}", ex);
                return [];
            }
        }

        public T? FindScalar<T>(string sql, object? parameters = default)
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var result = _context.DbConnection.QueryFirstOrDefault<T>(sql, parameters, _transaction, _timeOut);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"FindScalar（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public async Task<T?> FindScalarAsync<T>(string sql, object? parameters = default)
        {
            if (sql.IsNullOrEmpty()) return default;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var result = await _context.DbConnection.QueryFirstOrDefaultAsync<T>(sql, parameters, _transaction, _timeOut);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"FindScalarAsync（SQL）,{sql},{ex.Message}", ex);
                return default;
            }
        }

        public PagedList<T> Page<T>(string sql, int pageIndex, int pageSize, object? parameters = default)
        {
            var result = new PagedList<T>(pageIndex, pageSize);
            if (sql.IsNullOrEmpty()) return result;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var tempSql = GetSqlGenerater().GetPageSql(sql, pageIndex, pageSize, out var countSql);
                var totalCount = _context.DbConnection.ExecuteScalar<int>(countSql, null, _transaction, _timeOut);
                if (totalCount > 0)
                {
                    var list = _context.DbConnection.Query<T>(tempSql, parameters, _transaction, true, _timeOut);
                    var totalPages = CalculateTotalPages(totalCount, pageSize);
                    result.TotalCount = totalCount;
                    result.Items = list;
                    result.IsHaveFrontPage = pageIndex > 1;
                    result.IsHaveNextPage = pageIndex < totalPages;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Page（SQL）,{sql},{ex.Message}", ex);
                return result;
            }
        }

        public async Task<PagedList<T>> PageAsync<T>(string sql, int pageIndex, int pageSize, object? parameters = default)
        {
            var result = new PagedList<T>(pageIndex, pageSize);
            if (sql.IsNullOrEmpty()) return result;

            try
            {
                var _context = GetEFCoreContext(DbReadWriteType.Read);
                var tempSql = GetSqlGenerater().GetPageSql(sql, pageIndex, pageSize, out var countSql);
                var totalCount = await _context.DbConnection.ExecuteScalarAsync<int>(countSql, null, _transaction, _timeOut).ConfigureAwait(false);
                if (totalCount > 0)
                {
                    var list = await _context.DbConnection.QueryAsync<T>(tempSql, parameters, _transaction, _timeOut).ConfigureAwait(false);
                    var totalPages = CalculateTotalPages(totalCount, pageSize);
                    result.TotalCount = totalCount;
                    result.Items = list;
                    result.IsHaveFrontPage = pageIndex > 1;
                    result.IsHaveNextPage = pageIndex < totalPages;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"PageAsync（SQL）,{sql},{ex.Message}", ex);
                return result;
            }
        }

        public bool ExecuteSql(string sql, object? parameters = default)
        {
            if (sql.IsNullOrEmpty()) return false;

            try
            {
                var _context = GetEFCoreContext();
                var count = _context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"ExecuteSql（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ExecuteSqlAsync(string sql, object? parameters = default)
        {
            if (sql.IsNullOrEmpty()) return false;

            try
            {
                var _context = GetEFCoreContext();
                var count = await _context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut).ConfigureAwait(false);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"ExecuteSqlAsync（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public bool ExecuteSqlList(List<string> sqlList, object? parameters = default, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;
            try
            {
                context = GetEFCoreContext(DbReadWriteType.Write);
                var connection = context.DbConnection;
                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalCount = sqlList.Count;
                int totalBatches = CalculateTotalPages(totalCount, batchSize);

                for (int batchIndex = 1; batchIndex <= totalBatches; batchIndex++)
                {
                    var batchSqlList = sqlList.Skip((batchIndex - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batchSqlList) + ";";
                    int affectedRows = connection.Execute(batchSql, parameters, transaction, _timeOut);
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
                var sqlLog = sqlList.Count > 10 ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）" : string.Join(",", sqlList);
                _logger.Error(sqlLog, ex);
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

        public async Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = default, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false; // 标记是否是当前方法创建的事务
            try
            {
                context = GetEFCoreContext(DbReadWriteType.Write);
                var connection = context.DbConnection;
                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)await context.Database.BeginTransactionAsync();
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalCount = sqlList.Count;
                int totalBatches = CalculateTotalPages(totalCount, batchSize);

                for (int batchIndex = 1; batchIndex <= totalBatches; batchIndex++)
                {
                    var batchSqlList = sqlList.Skip((batchIndex - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batchSqlList) + ";";
                    int affectedRows = await connection.ExecuteAsync(batchSql, parameters, transaction, _timeOut);
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
                var sqlLog = sqlList.Count > 10 ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）" : string.Join(",", sqlList);
                _logger.Error(sqlLog, ex);
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
                context = GetEFCoreContext(DbReadWriteType.Write);
                var connection = context.DbConnection;
                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var sqlItem in sqlList)
                {
                    if (!sqlItem.Key.IsNullOrEmpty())
                    {
                        connection.Execute(sqlItem.Key, sqlItem.Value, transaction, _timeOut);
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
                var sqlLog = sqlList.Count > 10 ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）" : string.Join(",", sqlList);
                _logger.Error(sqlLog, ex);
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
            bool isSelfCreatedTxn = false; // 标记是否是当前方法创建的事务
            try
            {
                context = GetEFCoreContext(DbReadWriteType.Write);
                var connection = context.DbConnection;
                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)await context.Database.BeginTransactionAsync();
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var sqlItem in sqlList)
                {
                    if (!sqlItem.Key.IsNullOrEmpty())
                    {
                        await connection.ExecuteAsync(sqlItem.Key, sqlItem.Value, transaction, _timeOut);
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
                var sqlLog = sqlList.Count > 10 ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）" : string.Join(",", sqlList);
                _logger.Error(sqlLog, ex);
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

        public bool BeginTransaction()
        {
            if (_transaction != null) return true;

            try
            {
                _transaction = (IDbTransaction)GetEFCoreContext().Database.BeginTransaction();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"BeginTransaction,{ex.Message}", ex);
                return false;
            }
        }

        public void CommitTransaction()
        {
            if (_transaction == null) return;
            DbContext? context = null;

            try
            {
                context = GetEFCoreContext();
                context.Database.CommitTransaction();
                _transaction.Dispose();
                _transaction = null;
            }
            catch (Exception ex)
            {
                context?.Database.RollbackTransaction();
                _transaction?.Dispose();
                _transaction = null;
                _logger.Error($"CommitTransaction,{ex.Message}", ex);
            }
        }

        public void RollbackTransaction()
        {
            if (_transaction == null) return;
            try
            {
                var context = GetEFCoreContext();
                context.Database.RollbackTransaction();
                _transaction.Dispose();
                _transaction = null;
            }
            catch (Exception ex)
            {
                _transaction?.Dispose();
                _transaction = null;
                _logger.Error($"RollbackTransaction,{ex.Message}", ex);
            }
        }

        public async Task<bool> BeginTransactionAsync()
        {
            if (_transaction != null) return true;

            try
            {
                _transaction = (IDbTransaction)GetEFCoreContext().Database.BeginTransactionAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"BeginTransactionAsync,{ex.Message}", ex);
                return false;
            }
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction == null) return;
            DbContext? context = null;
            try
            {
                context = GetEFCoreContext();
                await context.Database.CommitTransactionAsync();
                _transaction.Dispose();
                _transaction = null;
            }
            catch (Exception ex)
            {
                if (context != null)
                {
                    await context.Database.RollbackTransactionAsync();
                }
                _transaction?.Dispose();
                _transaction = null;
                _logger.Error($"CommitTransactionAsync,{ex.Message}", ex);
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction == null) return;
            try
            {
                var context = GetEFCoreContext();
                await context.Database.RollbackTransactionAsync();
                _transaction.Dispose();
                _transaction = null;
            }
            catch (Exception ex)
            {
                _transaction?.Dispose();
                _transaction = null;
                _logger.Error($"RollbackTransactionAsync,{ex.Message}", ex);
            }
        }

        public IVivDbContext CreateContext(DatabaseOptions options)
        {
            var dataContext = new VivDatabaseContext(_vivContext, _logger);
            dataContext.SetOptions(options);
            return dataContext;
        }

        public void ChangeTenant(long tenantId)
        {
            if (tenantId > 0)
            {
                TenantId = tenantId;
            }
        }

        public void ChangeVivAppId(long vivAppId)
        {
            if (vivAppId > 0)
            {
                VivAppId = vivAppId;
            }
        }

        public void AutoSetValue(bool flag)
        {
            IsAutoSetValue = flag;
        }

        public ISqlGenerater GetSqlGenerater(DatabaseSouceType databaseSouce)
        {
            return SqlGeneraterFactory.GetSqlGenerater(databaseSouce);
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
                _transaction?.Dispose();
                _writeDbContext?.Dispose();
                _readDbContext?.Dispose();

                _transaction = null;
                _writeDbContext = null;
                _readDbContext = null;
            }

            _disposed = true;
        }
    }
}
