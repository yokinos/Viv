using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Momo.Core
{
    public class VivDatabase
    {
        protected readonly IVivContext _vivContext;
        protected DatabaseOptions _options;
        protected readonly IVivLogger _logger;

        protected EFAppContext? _writeDbContext;
        protected EFAppContext? _readDbContext;

        protected IDbTransaction? _transaction;
        protected int _timeOut = 30;
        protected static readonly HashSet<string> _primaryKeys = ["Id"];

        public VivDatabase(IVivContext vivContext, IVivLogger logger)
        {
            ArgumentNullException.ThrowIfNull(_vivContext);
            _vivContext = vivContext;
            AppId = _vivContext.AppId;
            TenantId = _vivContext.TenantId;
            _logger = logger;

            SetOptions();
        }

        public void SetOptions(DatabaseOptions? options = null)
        {
            options ??= VivConfigRegistry.Get<DatabaseOptions>();
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
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
            var context = CreateEFAppContext(_options, dbReadWriteType);
            if (context.Database.GetCommandTimeout() != _timeOut)
            {
                context.Database.SetCommandTimeout(_timeOut);
            }
            return context;
        }

        /// <summary>
        /// 创建EFAppContext
        /// </summary>
        /// <param name="options">数据库相关配置项</param>
        /// <param name="isRead">是否是创建读库</param>
        /// <param name="reload">是否重新实例化</param>
        /// <returns></returns>
        public EFAppContext CreateEFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType, bool reload = false)
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
        public long AppId { get; protected set; }

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
                    entityBase.AppId = AppId;
                    if (entityBase.Id == default)
                    {
                        entityBase.Id = IdMagic.NextId();
                    }
                }
            }
        }

        /// <summary>
        /// 适配数据库的字段名
        /// </summary>
        /// <param name="fieldName">原始字段名（如Amount/Status）</param>
        /// <returns>适配数据库的字段名</returns>
        public string AdaptFieldNameToDatabase(string fieldName)
        {
            return _options.DatabaseSouce switch
            {
                DatabaseSouceType.PostgreSQL => fieldName.ToLowerInvariant(),
                _ => fieldName
            };
        }

        #endregion

        #region 批量处理

        /// <summary>
        /// 单次EF处理实体的最大数量（超过这个数量会用Dapper处理）
        /// </summary>
        protected const int EFMaxCount = 500;

        public static int CalculateTotalPages(int totalItems, int pageSize)
        {
            if (totalItems < 0 || pageSize <= 0)
                return 0;

            //return (int)Math.Ceiling((double)totalItems / pageSize);
            return (totalItems + pageSize - 1) / pageSize;
        }

        #endregion
    }
}