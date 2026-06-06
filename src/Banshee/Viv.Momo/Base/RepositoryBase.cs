using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Momo.Base
{
    /// <summary>
    /// 通用仓库基类
    /// </summary>
    public abstract class RepositoryBase
    {
        protected readonly IVivContext _vivContext;
        protected readonly IMomoDbContext _dbContext;

        public RepositoryBase(IVivContext context, IMomoDbContext dbContext)
        {
            _vivContext = context;
            _dbContext = dbContext;
        }

    }
}
