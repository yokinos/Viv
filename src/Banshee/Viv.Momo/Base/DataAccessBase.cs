using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Momo.Base
{
    /// <summary>
    /// 仓促实现的基类，用于数据访问层的基类，提供一些通用的方法和属性。
    /// </summary>
    public abstract class DataAccessBase
    {
        protected readonly IVivContext _vivContext;
        protected readonly IMomoDbContext _dbContext;
        protected readonly ILoggerContract _logger;

        public DataAccessBase(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
        {
            _vivContext = context;
            _dbContext = dbContext;
            _logger = logger;
        }
    }
}
