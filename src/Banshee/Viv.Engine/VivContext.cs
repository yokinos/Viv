using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Engine
{
    public class VivContext : IVivContext
    {
        private readonly AsyncLocal<long> _tenantId = new();

        public long TenantId { get => _tenantId.Value; set => _tenantId.Value = value; }

        public void Clear()
        {
            _tenantId.Value = 0;
        }
    }
}
