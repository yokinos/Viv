using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Interface
{
    public interface IConnectionSelect
    {
        Task<string[]> GetConnectionStrings(long appId, long tenantId);
    }
}
