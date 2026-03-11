using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Engine
{

    public class VivContext : IVivContext
    {
        private static readonly AsyncLocal<long> _appId = new();
        private static readonly AsyncLocal<long> _tenantId = new();
        private static readonly AsyncLocal<long> _userId = new();

        public long AppId => _appId.Value;
        public long TenantId => _tenantId.Value;
        public long UserId => _userId.Value;

        public void Clear()
        {
            _appId.Value = 0;
            _tenantId.Value = 0;
            _userId.Value = 0;
        }

        public void SetAppId(long appId)
        {
            _appId.Value = appId;
        }

        public void SetTenantId(long tenantId)
        {
            _tenantId.Value = tenantId;
        }

        public void SetUserId(long userId)
        {
            _userId.Value = userId;
        }
    }
}
