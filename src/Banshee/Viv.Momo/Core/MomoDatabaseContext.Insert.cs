using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Viv.Delusion.Extension;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
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
            // 先物化再判空：IsNullOrEmpty 对惰性源（LINQ 查询/IQueryable）会先枚举一遍，ToList 又枚举一遍 → 二次枚举
            var entityList = entities?.ToList() ?? [];
            if (entityList.Count == 0) return false;

            try
            {
                AutoSetValue(entityList.ToArray());
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = context.SaveChanges();
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _options.DatabaseSource);
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
            // 先物化再判空：IsNullOrEmpty 对惰性源（LINQ 查询/IQueryable）会先枚举一遍，ToList 又枚举一遍 → 二次枚举
            var entityList = entities?.ToList() ?? [];
            if (entityList.Count == 0) return false;

            try
            {
                AutoSetValue(entityList.ToArray());
                var context = GetAppContext();
                int affected;

                if (entityList.Count < EFMaxCount)
                {
                    context.AddRange(entityList);
                    affected = await context.SaveChangesAsync();
                }
                else
                {
                    var tableName = SqlMagic.GetTableName<T>(_options.DatabaseSource);
                    var tempSql = SqlMagic.GetInsertSqlTemplate(tableName, typeof(T), _options.DatabaseSource);
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
    }
}
