using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
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
            // 先物化再判空：避免惰性源二次枚举
            var entityList = entities?.Where(x => x.Id > 0).ToList() ?? [];
            if (entityList.Count == 0) return false;

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

        public async Task<bool> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity
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

                var count = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"UpdateAsync,{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public async Task<bool> UpdateAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            // 先物化再判空：避免惰性源二次枚举
            var entityList = entities?.Where(x => x.Id > 0).ToList() ?? [];
            if (entityList.Count == 0) return false;

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

        private static int EFBatchUpdate<T>(List<T> entities, EFAppContext context) where T : class, IEntity
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

        private static async Task<int> EFBatchUpdateAsync<T>(List<T> entities, EFAppContext context, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            var entityIds = entities.Select(e => e.Id).Distinct().ToList();
            // 异步方法里用 ToListAsync，避免同步阻塞线程池线程等待 DB I/O
            var existingEntities = await context.Set<T>().Where(e => entityIds.Contains(e.Id)).ToListAsync(cancellationToken);

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

            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                count += await context.DbConnection.ExecuteAsync(item.Key, item.Value, _transaction, _timeOut).ConfigureAwait(false);
            }
            return count;
        }

        private List<KeyValueItem<string, DynamicParameters>> BuildUpdateSqlList<T>(List<T> entities, int pageSize = 200) where T : class, IEntity
        {
            var type = typeof(T);
            var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
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

                    var dbField = SqlMagic.QuoteIdentifier(propName, _options.DatabaseSource);
                    var idField = SqlMagic.QuoteIdentifier("Id", _options.DatabaseSource);

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

                sqlBuilder.Append($" WHERE {SqlMagic.QuoteIdentifier("Id", _options.DatabaseSource)} IN ({string.Join(", ", idParams)})");
                result.Add(new KeyValueItem<string, DynamicParameters>(sqlBuilder.ToString(), parameters));
            }

            return result;
        }

        #endregion
    }
}
