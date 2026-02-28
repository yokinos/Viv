using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Contexts;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Momo
{
    public class VivDatabase
    {
        protected readonly IVivContext _vivContext;
        protected readonly DatabaseOptions _options;
        protected readonly IVivLogger _logger;

        private EFAppContext _writeDbContext;
        private EFAppContext _readDbContext;

        public VivDatabase(IVivContext vivContext, IVivLogger logger)
        {
            ArgumentNullException.ThrowIfNull(_vivContext);
            _vivContext = vivContext;
            var options = VivConfigRegistry.Get<DatabaseOptions>();
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
            VivAppId = _vivContext.VivAppId;
            TenantId = _vivContext.TenantId;

            _logger = logger;
        }

        #region 实例化EFCore

        /// <summary>
        /// 获取要使用的EFCore上下文
        /// </summary>
        /// <param name="dbReadWriteType"></param>
        /// <returns></returns>
        [return: NotNull]
        public EFAppContext GetEFCoreContext(DbReadWriteType dbReadWriteType = DbReadWriteType.Write)
        {
            return CreateEFAppContext(_options, dbReadWriteType);
        }

        /// <summary>
        /// 创建EFAppContext
        /// </summary>
        /// <param name="options">数据库相关配置项</param>
        /// <param name="isRead">是否是创建读库</param>
        /// <param name="reload">是否重新实例化</param>
        /// <returns></returns>
        public EFAppContext CreateEFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType = DbReadWriteType.Read, bool reload = false)
        {
            if (!options.IsReadWriteSplit)
            {
                // 如果没有开启读写分离 默认都是写库
                dbReadWriteType = DbReadWriteType.Write;
            }

            if (dbReadWriteType == DbReadWriteType.Read)
            {
                if (_readDbContext == null || reload)
                {
                    _readDbContext?.Dispose();
                    _readDbContext = new EFAppContext(options, DbReadWriteType.Read);
                }

                return _readDbContext;
            }
            else
            {
                if (_writeDbContext == null || reload)
                {
                    _writeDbContext?.Dispose();
                    _writeDbContext = new EFAppContext(options, DbReadWriteType.Write);
                }

                return _writeDbContext;
            }
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// 是否自动设置默认值
        /// </summary>
        public bool IsAutoSetValue { get; protected set; } = true;

        /// <summary>
        /// 当前实例的AppId
        /// </summary>
        public long VivAppId { get; protected set; }

        /// <summary>
        /// 当前实例的TenantId
        /// </summary>
        public long TenantId { get; protected set; }

        #endregion

        #region 公共方法

        /// <summary>
        /// 自动设置默认值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <returns></returns>
        protected void AutoSetValue<T>(params T[] entitys)
        {
            if (entitys.IsNullOrEmpty() || !IsAutoSetValue) return;
            foreach (var entity in entitys)
            {
                if (entity is EntityBase entityBase)
                {
                    entityBase.TenantId = TenantId;
                    entityBase.VivAppId = VivAppId;
                    if (entityBase.Id == default)
                    {
                        entityBase.Id = IdMagic.NextId();
                    }
                    if (entityBase.CreatedAt == default)
                    {
                        entityBase.CreatedAt = DateTimeOffset.Now;
                    }
                }
            }
        }

        #endregion

        #region 批量处理

        /// <summary>
        /// 单次EF处理实体的最大数量（超过这个数量转SQL处理）
        /// </summary>
        private const int EFMaxCount = 1000;

        /// <summary>
        /// 单次SQL处理的最大数量（超过这个数量转命令处理）
        /// </summary>
        private const int SqlMaxCount = 10000;

        public int BatchExecute<T>(List<T> entitys, DbOperationType operationType)
        {
            AutoSetValue(entitys);
            return operationType switch
            {
                DbOperationType.Insert => BatchInsert(entitys),
            };
        }

        public int BatchInsert<T>(List<T> entitys)
        {
            var count = entitys.Count;
            if (count < EFMaxCount)
            {
                var context = GetEFCoreContext();
                context.AddRange(entitys);
                return context.SaveChanges();
            }

            if (count < SqlMaxCount)
            {
                // 用dapper批量插入
                var sqlList = new List<string>();
                foreach (var entity in entitys)
                {
                  var sql = _options.DatabaseSouce switch 
                    {
                         DatabaseSouceType.PostgreSQL => Posgresqlma:
                        
                         DatabaseSouceType.MsSql:
                    
                    }

                    sqlList.Add()
                }

            }

        }

        #endregion
    }
}