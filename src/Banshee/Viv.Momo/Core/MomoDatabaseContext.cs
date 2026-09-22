using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Log;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Momo.Sync;

namespace Viv.Momo.Core
{
    /// <summary>
    /// Viv 框架下的数据库访问实现（基于 EFCore 与 Dapper，支持 PostgreSQL、SqlServer）
    /// </summary>
    public partial class MomoDatabaseContext : MomoDatabase, IMomoDbContext
    {
        private bool _disposed;

        public MomoDatabaseContext(IVivContext vivContext, ILoggerContract logger, IDatabaseOptionsProvider optionsProvider)
            : base(vivContext, logger, optionsProvider) { }

        #region Insert

        public bool Insert<T>(T entity) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetInsertValue(entity);
                var context = GetAppContext();
                context.Add(entity);
                var count = context.SaveChanges();
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Insert,{ex.Message},{entity.ToJson()}", ex);
            }
        }

        public bool Insert<T>(IEnumerable<T> entities) where T : IEntity
        {
            // 先物化再判空：IsNullOrEmpty 对惰性源（LINQ 查询/IQueryable）会先枚举一遍，ToList 又枚举一遍 → 二次枚举
            var entityList = entities?.ToList() ?? [];
            if (entityList.Count == 0) return false;

            try
            {
                AutoSetInsertValue(entityList.ToArray());
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = context.SaveChanges();
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _databaseOptions.DatabaseSource);
                    affected = context.DbConnection.Execute(tempSql, entityList, _transaction, _timeOut);
                }

                return affected > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Insert（批量）,{ex.Message}", ex);
            }
        }

        public async Task<bool> InsertAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                AutoSetInsertValue(entity);
                var context = GetAppContext();
                context.Add(entity);
                var count = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"InsertAsync,{ex.Message},{entity.ToJson()}", ex);
            }
        }

        public async Task<bool> InsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : IEntity
        {
            // 先物化再判空：IsNullOrEmpty 对惰性源（LINQ 查询/IQueryable）会先枚举一遍，ToList 又枚举一遍 → 二次枚举
            var entityList = entities?.ToList() ?? [];
            if (entityList.Count == 0) return false;

            try
            {
                AutoSetInsertValue(entityList.ToArray());
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _databaseOptions.DatabaseSource);
                    affected = await context.DbConnection.ExecuteAsync(tempSql, entityList, _transaction, _timeOut).ConfigureAwait(false);
                }

                return affected > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"InsertAsync（批量）,{ex.Message}", ex);
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

                // 必须看得见被软删除的行，否则拿不到库里那一份、CopyProtectedValues 被整段跳过，
                // 入参上的 CreatedAt/CreatedBy/TenantId 默认值会盖掉库里的值。
                // 用开关而不是 IgnoreQueryFilters：T 只约束到 IEntity（不是 class），
                // context.Set&lt;T&gt;() 编译不过，Find 是 EF 的非泛型入口，只能靠开关放行。
                // 只放行软删除，租户那条照旧生效。
                using var filterScope = DataFilterSwitch.Disable(typeof(SoftDeletedFilter));
                var existingEntity = context.Find(typeof(T), entity.Id);
                if (existingEntity is T existing)
                {
                    // 入参上 CreatedAt/CreatedBy/TenantId 通常是 default，直接 SetValues 会把库里的值冲掉 ——
                    // 先把库里那一份补回入参，再连同盖章后的 Updated* 一起 SetValues
                    CopyProtectedValues(existing, entity);
                    AutoSetUpdateValue(entity);
                    context.Entry(existing).CurrentValues.SetValues(entity);
                }
                else
                {
                    AutoSetUpdateValue(entity);
                    context.Update(entity);
                }

                var count = context.SaveChanges();
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Update,{ex.Message},{entity.ToJson()}", ex);
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

                // 批量路径可能落到 Dapper（>EFMaxCount），那条路不加载库里的那一份、没法补回入参，
                // 所以 Updated* 在这里统一盖，两个分支都不漏
                AutoSetUpdateValue(entityList.ToArray());

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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Update（批量）,{entityList.Count},{ex.Message}", ex);
            }
        }

        public async Task<bool> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (entity == null) return false;

            try
            {
                var context = GetAppContext();

                // 同同步版：按 Id 取库里那一份要放行软删除过滤，否则 CopyProtectedValues 被跳过
                using var filterScope = DataFilterSwitch.Disable(typeof(SoftDeletedFilter));
                var existingEntity = await context.FindAsync(typeof(T), [entity.Id], cancellationToken).ConfigureAwait(false);
                if (existingEntity is T existing)
                {
                    // 同同步版：先补回不可变列再 SetValues，否则创建信息/租户被入参的 default 冲掉
                    CopyProtectedValues(existing, entity);
                    AutoSetUpdateValue(entity);
                    context.Entry(existing).CurrentValues.SetValues(entity);
                }
                else
                {
                    AutoSetUpdateValue(entity);
                    context.Update(entity);
                }

                var count = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"UpdateAsync,{ex.Message},{entity.ToJson()}", ex);
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

                // 同同步版：两个分支共用一次盖章
                AutoSetUpdateValue(entityList.ToArray());

                if (entityList.Count < EFMaxCount)
                {
                    count = await EFBatchUpdateAsync(entityList, context, cancellationToken);
                }
                else
                {
                    count = await DapperBatchUpdateAsync(entityList, context);
                }

                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"UpdateAsync（批量）,{entityList.Count},{ex.Message}", ex);
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
                    CopyProtectedValues(existing, entity);
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
                    CopyProtectedValues(existing, entity);
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

        /// <summary>
        /// 批量 Update 必须跳过的列：创建信息只写一次、租户不允许被改。
        ///
        /// 单条 Update 能靠 <see cref="CopyProtectedValues"/> 把库里那一份补回入参，但批量路径根本不加载
        /// 库里的那一份（<see cref="BuildUpdateSqlList"/> 直接按入参拼 SQL），没有可补的来源 ——
        /// 照常写下去就是把每一行的创建信息冲成 NULL、把行搬到租户 0。
        /// 所以这里改成不写这几列（<c>ELSE {dbField} END</c> 自然保留库里的原值）。
        ///
        /// 与 <see cref="CopyProtectedValues"/> 是同一份清单，但判据不同：那边按实例的类型判定，
        /// 这边只有 <c>typeof(T)</c>，只能问「T 是否实现了该契约」。
        /// </summary>
        private static readonly (Type Contract, string Property)[] _protectedColumns =
        [
            (typeof(ITenant), nameof(ITenant.TenantId)),
            (typeof(ICreatedAt), nameof(ICreatedAt.CreatedAt)),
            (typeof(ICreatedBy), nameof(ICreatedBy.CreatedBy)),
        ];

        private static bool IsProtectedColumn(Type type, string propertyName)
        {
            foreach (var (contract, property) in _protectedColumns)
            {
                if (string.Equals(property, propertyName, StringComparison.OrdinalIgnoreCase)
                    && contract.IsAssignableFrom(type))
                {
                    return true;
                }
            }

            return false;
        }

        private List<KeyValueItem<string, DynamicParameters>> BuildUpdateSqlList<T>(List<T> entities, int pageSize = 200) where T : class, IEntity
        {
            var type = typeof(T);
            var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
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
                    if (IsProtectedColumn(type, propName)) continue;

                    var dbField = SqlMagic.QuoteIdentifier(propName, _databaseOptions.DatabaseSource);
                    var idField = SqlMagic.QuoteIdentifier("Id", _databaseOptions.DatabaseSource);

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

                sqlBuilder.Append($" WHERE {SqlMagic.QuoteIdentifier("Id", _databaseOptions.DatabaseSource)} IN ({string.Join(", ", idParams)})");
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Delete,{ex.Message},{entity.ToJson()}", ex);
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
                    var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                    var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T)) && TenantId > 0;
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _databaseOptions.DatabaseSource)} IN @Ids"
                        + (isTenantEntity ? $" AND {SqlMagic.QuoteIdentifier("TenantId", _databaseOptions.DatabaseSource)} = @TenantId" : "");
                    int affected = context.DbConnection.Execute(deleteSql, isTenantEntity ? new { Ids = ids, TenantId } : new { Ids = ids }, _transaction, _timeOut);
                    return affected > 0;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Delete（批量）,{ex.Message}", ex);
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
                var affected = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return affected > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"DeleteAsync,{ex.Message},{entity.ToJson()}", ex);
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
                    int affected = await context.Set<T>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                    return affected > 0;
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                    var isTenantEntity = typeof(ITenant).IsAssignableFrom(typeof(T)) && TenantId > 0;
                    var deleteSql = $"DELETE FROM {tableName} WHERE {SqlMagic.QuoteIdentifier("Id", _databaseOptions.DatabaseSource)} IN @Ids"
                        + (isTenantEntity ? $" AND {SqlMagic.QuoteIdentifier("TenantId", _databaseOptions.DatabaseSource)} = @TenantId" : "");
                    int affected = await context.DbConnection.ExecuteAsync(deleteSql, isTenantEntity ? new { Ids = ids, TenantId } : new { Ids = ids }, _transaction, _timeOut).ConfigureAwait(false);
                    return affected > 0;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"DeleteAsync（批量）,{ex.Message}", ex);
            }
        }

        public bool Delete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Delete（委托）,{ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var command = new CommandDefinition(sql, parameters, _transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var count = await context.DbConnection.ExecuteAsync(command).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"DeleteAsync（委托）,{ex.Message}", ex);
            }
        }

        public bool Delete<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return false;

            try
            {
                var context = GetAppContext();
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameter) = SqlMagic.GetDeleteSql<T>(tableName, x => x.Id == id, _databaseOptions.DatabaseSource, TenantId);
                var count = context.DbConnection.Execute(sql, parameter, _transaction, _timeOut);
                return (count > 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Delete (Id),{ex.Message},{id}", ex);
            }
        }

        public async Task<bool> DeleteAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (id <= 0) return false;

            try
            {
                var context = GetAppContext();
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameter) = SqlMagic.GetDeleteSql<T>(tableName, x => x.Id == id, _databaseOptions.DatabaseSource, TenantId);
                var command = new CommandDefinition(sql, parameter, _transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var count = await context.DbConnection.ExecuteAsync(command).ConfigureAwait(false);
                return (count > 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"DeleteAsync (Id),{ex.Message},{id}", ex);
            }
        }

        #endregion

        #region SoftDelete

        public bool SoftDelete<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity, ISoftDeleted
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext(DbReadWriteType.Write);
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SoftDelete（委托）,{ex.Message}", ex);
            }
        }

        public async Task<bool> SoftDeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity, ISoftDeleted
        {
            if (predicate == null) return false;

            try
            {
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                if (string.IsNullOrEmpty(sql)) return false;

                var context = GetAppContext();
                var command = new CommandDefinition(sql, parameters, _transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var count = await context.DbConnection.ExecuteAsync(command).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SoftDeleteAsync（委托）,{ex.Message}", ex);
            }
        }

        public bool SoftDelete<T>(long id) where T : class, IEntity, ISoftDeleted
        {
            if (id <= 0) return false;

            try
            {
                Expression<Func<T, bool>> predicate = x => x.Id == id;
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                var context = GetAppContext(DbReadWriteType.Write);
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SoftDelete（Id）,{ex.Message}", ex);
            }
        }

        public async Task<bool> SoftDeleteAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity, ISoftDeleted
        {
            if (id <= 0) return false;

            try
            {
                Expression<Func<T, bool>> predicate = x => x.Id == id;
                var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);
                var (sql, parameters) = SqlMagic.GetSoftDeleteSql(tableName, predicate, _databaseOptions.DatabaseSource, TenantId);
                var context = GetAppContext(DbReadWriteType.Write);
                var command = new CommandDefinition(sql, parameters, _transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var count = await context.DbConnection.ExecuteAsync(command).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SoftDeleteAsync（Id）,{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"ExecuteSql（SQL）,{sql},{ex.Message}", ex);
            }
        }

        public async Task<bool> ExecuteSqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sql)) return false;

            try
            {
                var context = GetAppContext();
                var command = new CommandDefinition(sql, parameters, _transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var count = await context.DbConnection.ExecuteAsync(command).ConfigureAwait(false);
                return count > 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"ExecuteSqlAsync（SQL）,{sql},{ex.Message}", ex);
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
                    transaction = _transaction ?? GetDbTransaction(context.Database.BeginTransaction());
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
            catch (OperationCanceledException)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }
                throw;
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
                throw WrapDatabaseException(log, ex);
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = null, bool isTxn = true, CancellationToken cancellationToken = default)
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
                    // 获取底层 ADO 事务
                    transaction = _transaction ?? GetDbTransaction(await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalPages = CalculateTotalPages(sqlList.Count, batchSize);
                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = sqlList.Skip((page - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batch) + ";";
                    var command = new CommandDefinition(batchSql, parameters, transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                    await connection.ExecuteAsync(command).ConfigureAwait(false);
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }
                throw;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList);
                throw WrapDatabaseException(log, ex);
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
                    transaction = _transaction ?? GetDbTransaction(context.Database.BeginTransaction());
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
            catch (OperationCanceledException)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }
                throw;
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
                throw WrapDatabaseException(log, ex);
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true, CancellationToken cancellationToken = default)
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
                    transaction = _transaction ?? GetDbTransaction(await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var item in sqlList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        var command = new CommandDefinition(item.Key, item.Value, transaction, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                        await connection.ExecuteAsync(command).ConfigureAwait(false);
                    }
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }
                throw;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10).Select(x => x.Key))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList.Select(x => x.Key));
                throw WrapDatabaseException(log, ex);
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

        #region Query (Exist, Count, Single, First, Find, List)

        public bool Exist<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return context.Set<T>().Any(predicate);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Exist（委托）,{ex.Message}", ex);
            }
        }

        public async Task<bool> ExistAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (predicate == null) return false;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().AnyAsync(predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"ExistAsync（委托）,{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Count（委托）,{ex.Message}", ex);
            }
        }

        public async Task<int> CountAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (predicate == null) return -1;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                return await context.Set<T>().CountAsync(predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"CountAsync（委托）,{ex.Message}", ex);
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
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SingleOrDefault（委托）,{ex.Message}", ex);
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
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SingleOrDefault（SQL）,{sql},{ex.Message}", ex);
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
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SingleOrDefaultAsync（委托）,{ex.Message}", ex);
            }
        }

        public async Task<T?> SingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var command = new CommandDefinition(sql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                return await connection.QuerySingleOrDefaultAsync<T>(command).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                WriteLog($"SingleOrDefaultAsync（SQL）: 实体{typeof(T).Name}符合条件的记录超过1条，SQL：{sql}，异常：{ex.Message}", ex);
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"SingleOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 本次按主键读 T 时生效的读过滤器：管这个实体、且没被开关关掉的。
        /// 与 EF 挂模型那条路径取法一致。
        /// </summary>
        private static List<IMomoDataFilter> GetActiveFilters<T>()
        {
            var active = new List<IMomoDataFilter>(MomoDataFilters.All.Count);
            foreach (var filter in MomoDataFilters.All)
            {
                if (filter.AppliesTo(typeof(T)) && !filter.IsDisabled)
                {
                    active.Add(filter);
                }
            }
            return active;
        }

        [return: MaybeNull]
        public T? Find<T>(long id) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                // 参数由过滤器自己往字典里放（租户那条放 TenantId），SQL 也由它拼。
                // 传 TenantId 而不是 _vivContext.SubjectId，ChangeTenant 的覆盖要算数
                var parameters = new Dictionary<string, object> { ["Id"] = id };
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _databaseOptions.DatabaseSource, TenantId, GetActiveFilters<T>(), parameters);
                return connection.QueryFirstOrDefault<T>(sql, parameters, null, _timeOut);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Find,Table:{tableName},Id:{id},{ex.Message}", ex);
            }
        }

        public async Task<T?> FindAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IEntity
        {
            if (id <= 0) return default;
            var tableName = SqlMagic.GetTableName<T>(_databaseOptions.DatabaseSource);

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var parameters = new Dictionary<string, object> { ["Id"] = id };
                var sql = SqlMagic.GetFindSqlTemplate(tableName, _databaseOptions.DatabaseSource, TenantId, GetActiveFilters<T>(), parameters);
                var command = new CommandDefinition(sql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<T>(command).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindAsync,Table:{tableName},Id:{id},{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FirstOrDefault（委托）,{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FirstOrDefault（SQL）,{sql},{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FirstOrDefaultAsync（委托）,{ex.Message}", ex);
            }
        }

        public async Task<T?> FirstOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var connection = context.DbConnection;
                var command = new CommandDefinition(sql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<T>(command).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FirstOrDefaultAsync（SQL）,{sql},{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindList（委托）,{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindList（SQL）,{sql},{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindListAsync（委托）,{ex.Message}", ex);
            }
        }

        public async Task<List<T>> FindListAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(sql)) return [];

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var command = new CommandDefinition(sql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var result = await context.DbConnection.QueryAsync<T>(command).ConfigureAwait(false);
                return result.ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindListAsync（SQL）,{sql},{ex.Message}", ex);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindScalar（SQL）,{sql},{ex.Message}", ex);
            }
        }

        public async Task<T?> FindScalarAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sql)) return default;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var command = new CommandDefinition(sql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var result = await context.DbConnection.QueryFirstOrDefaultAsync<T>(command).ConfigureAwait(false);
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"FindScalarAsync（SQL）,{sql},{ex.Message}", ex);
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
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _databaseOptions.DatabaseSource);
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"Page（SQL）,{sql},{ex.Message}", ex);
            }
        }

        public async Task<PagedList<T>> PageAsync<T>(string sql, int pageIndex, int pageSize, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = new PagedList<T>(pageIndex, pageSize);
            if (string.IsNullOrEmpty(sql)) return result;

            try
            {
                var context = GetAppContext(DbReadWriteType.Read);
                var (pageSql, countSql) = SqlMagic.GetPageSqlTemplate(sql, pageIndex, pageSize, _databaseOptions.DatabaseSource);
                var countCommand = new CommandDefinition(countSql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                var totalCount = await context.DbConnection.ExecuteScalarAsync<int>(countCommand).ConfigureAwait(false);
                if (totalCount > 0)
                {
                    var totalPages = CalculateTotalPages(totalCount, pageSize);
                    var pageCommand = new CommandDefinition(pageSql, parameters, null, _timeOut, null, CommandFlags.Buffered, cancellationToken);
                    var list = await context.DbConnection.QueryAsync<T>(pageCommand).ConfigureAwait(false);
                    result.TotalCount = totalCount;
                    result.Items = list;
                    result.IsHaveFrontPage = pageIndex > 1;
                    result.TotalPages = totalPages;
                    result.IsHaveNextPage = pageIndex < totalPages;
                }
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw WrapDatabaseException($"PageAsync（SQL）,{sql},{ex.Message}", ex);
            }
        }

        #endregion

        #region Other

        public IMomoDbContext? CreateContext(DatabaseOptions options)
        {
            if (options == null) return null;
            var dataContext = new MomoDatabaseContext(_vivContext, _logger, _optionsProvider);
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

        public IDbConnection GetDbConnection(DbReadWriteType readWriteType = DbReadWriteType.Read)
        {
            return GetAppContext(readWriteType).DbConnection;
        }

        /// <summary>
        /// 按实体同步表结构：建缺失的表、加缺失的列。
        ///
        /// <paramref name="allowDrop"/> 控制是否允许删表/删列；<paramref name="allowAlterColumn"/> 控制
        /// 是否允许改已有列的类型/可空性（默认关 —— 这个判据对现有库误报极多，
        /// 见 <c>SchemaSynchronizer.GenerateDdl</c>）。两者都默认关，所以默认只会「加」，不会「改/删」。
        /// </summary>
        public async Task SyncTableAsync(bool allowDrop = false, bool allowAlterColumn = false, CancellationToken cancellationToken = default)
        {
            var context = GetAppContext(DbReadWriteType.Write);

            // 1. EF Core EnsureCreated：创建数据库中不存在的表
            await context.Database.EnsureCreatedAsync(cancellationToken);

            // 2. SchemaSynchronizer：处理列级变更
            var sync = new SchemaSynchronizer(_databaseOptions, allowAlterColumn: allowAlterColumn);
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

            // 同上，ALTER COLUMN 默认也不做（判据对现有库几乎全是误报，见 SchemaSynchronizer.GenerateDdl）。
            // 这里只负责「说出来」——真正的门开在 GenerateDdl 里那一处，不在这里再删一遍 diff，
            // 免得同一个开关有两个地方要同步改。
            if (!allowAlterColumn)
            {
                var alters = diff.ModifiedTables
                    .SelectMany(t => t.ColumnDiffs
                        .Where(c => c.Type == DiffType.Modified)
                        .Select(c => $"{t.TableName}.{c.ColumnName}"))
                    .ToList();
                if (alters.Count > 0)
                {
                    WriteLog($"SyncTable: skip ALTER {alters.Count} column(s): {string.Join(", ", alters)}", null!);
                }
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
