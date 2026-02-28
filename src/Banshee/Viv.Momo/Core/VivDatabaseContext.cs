using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
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

        #region Insert

        public bool Insert<T>(T entity)
        {
            if (entity == null)
            {
                return false;
            }

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
            if (entitys.IsNullOrEmpty())
            {
                return false;
            }

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
            if (entity == null)
            {
                return false;
            }

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
            if (entitys.IsNullOrEmpty())
            {
                return false;
            }

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

        #endregion

        #region Update

        public bool Update<T>(T entity) where T : IEntity
        {
            if (entity == null)
            {
                return false;
            }

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
            if (entitys.IsNullOrEmpty())
            {
                return false;
            }

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
                _logger.Error($"Update（批量）：{entityList.Count},{ex.Message}", ex);
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

        private List<KeyValueItem<string, DynamicParameters>> BuilderUpdateSqlList<T>(List<T> entityList, int pageSize = 200)
        {
            var type = typeof(T);
            var tableName = AdaptFieldNameToDatabase(VivMomoMagic.GetTableName<T>());
            var pilist = type.GetProperties();
            var sqlList = new List<KeyValueItem<string, DynamicParameters>>();

            var totalCount = entityList.Count;
            var totalPages = CalculateTotalPages(totalCount, pageSize);
            for (int index = 1; index <= totalPages; index++)
            {
                var list = entityList.Skip((index - 1) * pageSize).Take(pageSize).ToList();
                var sql = new StringBuilder();
                sql.Append($"UPDATE {tableName} SET ");

                foreach (var pi in pilist)
                {
                    var field = pi.Name;
                    if (_primaryKeys.Contains(field))
                    {
                        continue;
                    }

                    var dbField = AdaptFieldNameToDatabase(field);
                    var idFiled = AdaptFieldNameToDatabase("Id");

                    sql.Append($"{dbField} = CASE {idFiled} ");

                    foreach (var entity in list)
                    {
                        var value = pi.GetValue(entity);


                    }

                }
            }

            return sqlList;
        }

        #endregion
    }
}
