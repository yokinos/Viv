using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Contexts;
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
        protected readonly DatabaseOptions _options;
        protected readonly IVivLogger _logger;

        private EFAppContext _writeDbContext;
        private EFAppContext _readDbContext;

        protected readonly DbTransaction _transaction;
        protected int _timeOut = 30;
        protected static readonly HashSet<string> _primaryKeys = ["Id"];

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

        protected ISqlGenerater GetSqlGenerater()
        {
            return SqlGeneraterFactory.GetSqlGenerater(_options.DatabaseSouce);
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
        /// 单次EF处理实体的最大数量（超过这个数量转SQL处理）
        /// </summary>
        protected const int EFMaxCount = 1000;


        /// <summary>
        /// 批量执行SQL语句（建议加事务执行）
        /// </summary>
        /// <param name="sqlList"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool DapperExecuteSqlList(List<string> sqlList, EFAppContext context, DynamicParameters? parameters = null, int pageSize = 200)
        {
            var totalCount = sqlList.Count;
            var totalPages = CalculateTotalPages(totalCount, pageSize);
            for (int index = 1; index <= totalPages; index++)
            {
                var list = sqlList.Skip((index - 1) * pageSize).Take(pageSize).ToList();
                var sql = string.Join(";", list) + ";";
                context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
            }

            return true;
        }

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