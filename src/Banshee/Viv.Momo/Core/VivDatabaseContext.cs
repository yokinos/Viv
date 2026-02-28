using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Momo.Contexts
{
    /// <summary>
    /// Viv 框架下的数据库访问实现 （基于EFCore与Dapper）(支持PostgreSQL,SqlServer)
    /// </summary>
    public class VivDatabaseContext : VivDatabase, IVivDbContext
    {
        public VivDatabaseContext(IVivContext vivContext, IVivLogger vivLogger) : base(vivContext, vivLogger)
        {

        }

        public bool Insert<T>(T entity)
        {
            try
            {
                if (entity == null)
                {
                    return false;
                }
                AutoSetValue(entity);
                var _context = GetEFCoreContext();
                _context.Add(entity);
                var count = _context.SaveChanges();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"{nameof(Insert)},{ex.Message},{entity.ToJson()}", ex);
                return false;
            }
        }

        public bool Insert<T>(IEnumerable<T> entitys)
        {
            try
            {
                if (entitys.IsNullOrEmpty())
                {
                    return false;
                }

                var allCount = entitys.Count();
                if(allCount)

            }
            catch (Exception ex)
            {
                return false;
            }
        }

    }
}
