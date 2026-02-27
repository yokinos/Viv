using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Momo.Contexts;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Magic;

namespace Viv.Momo
{
    public class VivDatabase
    {
        protected readonly IVivContext _vivContext;
        protected EFCoreContext _efDbContext;
        protected readonly DatabaseOptions _options;

        public VivDatabase(IVivContext vivContext)
        {
            ArgumentNullException.ThrowIfNull(_vivContext);
            _vivContext = vivContext;
            var options = VivConfigRegistry.Get<DatabaseOptions>();
            ArgumentNullException.ThrowIfNull(options);
            CreateEFCoreContext(options);
            VivAppId = _vivContext.VivAppId;
            TenantId = _vivContext.TenantId;
        }

        #region 实例化EFCore

        protected async void CreateEFCoreContext(DatabaseOptions options)
        {
            if (_efDbContext != null)
            {
                _efDbContext.Dispose();
                _efDbContext = null;
            }

            _efDbContext = new EFCoreContext(options);
        }

        protected async ValueTask CreateEFCoreContextAsync(DatabaseOptions options)
        {
            if (_efDbContext != null)
            {
                await _efDbContext.DisposeAsync();
                _efDbContext = null;
            }

            _efDbContext = new EFCoreContext(options);
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
        protected void AutoSetValue<T>(T entity)
        {
            if (entity == null || !IsAutoSetValue) return;

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

        #endregion
    }
}