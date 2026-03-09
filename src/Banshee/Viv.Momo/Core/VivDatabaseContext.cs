using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Generic;
using Viv.Vva.Magic;

namespace Viv.Momo.Contexts
{
    /// <summary>
    /// Viv 框架下的数据库访问实现 （基于EFCore与Dapper）(支持PostgreSQL,SqlServer)
    /// </summary>
    public class VivDatabaseContext : VivDatabase, IVivDbContext
    {
        public VivDatabaseContext(IVivContext vivContext, IVivLogger vivLogger)
            : base(vivContext, vivLogger) { }

        public bool Insert<T>(T entity)
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

        public bool Insert<T>(IEnumerable<T> entitys)
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
                    var tempsql = genarater.CreateInsertTemplateSql(VivMomoMagic.GetTableName<T>(), typeof(T));
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

        public async Task<bool> InsertAsync<T>(T entity)
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

        public async Task<bool> InsertAsync<T>(IEnumerable<T> entitys)
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
                    var tempsql = genarater.CreateInsertTemplateSql(VivMomoMagic.GetTableName<T>(), typeof(T));
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

        private int EFBatchUpdate<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
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

        private async Task<int> EFBatchUpdateAsync<T>(List<T> entitys, EFAppContext context) where T : class, IEntity
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
            var tableName = AdaptFieldNameToDatabase(VivMomoMagic.GetTableName<T>());
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
                    var deleteSql = $"DELETE FROM {VivMomoMagic.GetTableName<T>()} WHERE Id IN @Ids";
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
                    var deleteSql = $"DELETE FROM {VivMomoMagic.GetTableName<T>()} WHERE Id IN @Ids";
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
                return _context.Set<T>().AsNoTracking().SingleOrDefault(predicate);
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
        public T? SingleOrDefault<T>(string sql, DynamicParameters? parameters = default) where T : class
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
                _logger.Error($"SingleOrDefault（SQL）,{ex.Message}", ex);
                return default;
            }
        }

        private Expression<Func<T, bool>> AutoSpliceCommonCondition<T>(Expression<Func<T, bool>> predicate)
        {
            Expression<Func<T, bool>> finalPredicate = predicate;
            if (typeof(T).IsAssignableFrom(typeof(EntityBase)))
            {
                Expression<Func<T, bool>> softDeleteExpr = x => (x as EntityBase).IsDeleted == VivBool.False;
                Expression<Func<T, bool>> tenantExpr = x => (x as EntityBase).TenantId == TenantId;
                Expression<Func<T, bool>> appIdExpr = x => (x as EntityBase).VivAppId == VivAppId;

                finalPredicate = CombineExpressions(finalPredicate, softDeleteExpr);
                finalPredicate = CombineExpressions(finalPredicate, tenantExpr);
                finalPredicate = CombineExpressions(finalPredicate, appIdExpr);
            }

            return finalPredicate;
        }

        private Expression<Func<T, bool>> CombineExpressions<T>(Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var combined = Expression.AndAlso(
                new ParameterReplacer(param).Visit(expr1.Body),
                new ParameterReplacer(param).Visit(expr2.Body)
            );
            return Expression.Lambda<Func<T, bool>>(combined, param);
        }
    }
}
