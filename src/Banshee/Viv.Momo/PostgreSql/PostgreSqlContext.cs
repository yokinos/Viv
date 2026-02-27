using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Momo.Contexts;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Magic;

namespace Viv.Momo.PostgreSql
{
    public class PostgreSqlContext : VivDatabase, IVivDbContext
    {
        public PostgreSqlContext(IVivContext vivContext) : base(vivContext)
        {

        }

        public bool Insert<T>(T entity)
        {
            if (entity == null) return false;
            AutoSetValue(entity);
            _efDbContext.Add(entity);
            var count = _efDbContext.SaveChanges();
            return count > 0;
        }
    }
}
