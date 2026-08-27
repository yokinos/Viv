using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Vo.Tenant
{
    public class GetTenantOutput
    {
        public long TenantId { get; set; }

        public string TenantName { get; set; }

        public string TenantCode { get; set; }
    }
}
