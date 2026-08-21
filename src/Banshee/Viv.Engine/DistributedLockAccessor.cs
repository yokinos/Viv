using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// 分布式锁业务处理者
    /// </summary>
    public class DistributedLockAccessor //: IDistributedLock
    {
        private readonly IRedisService _redisService;
        private readonly ILoggerContract _logger;

        public DistributedLockAccessor(IRedisService redisService, ILoggerContract logger)
        {
            _redisService = redisService;
            _logger = logger;
        }


    }
}
