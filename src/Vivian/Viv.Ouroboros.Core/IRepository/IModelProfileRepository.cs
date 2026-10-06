using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Database.Ouroboros;

namespace Viv.Ouroboros.Core.IRepository
{
    /// <summary>
    /// 模型档位仓储（OtModelProfile）。只负责存取，不管缓存与解密。
    /// </summary>
    public interface IModelProfileRepository
    {
        /// <summary>按档位键取启用的行，按 Priority 升序（前者优先，失败可退到后者）</summary>
        Task<List<OtModelProfile>> GetEnabledByKeyAsync(string profileKey);

        /// <summary>取所有启用的档位</summary>
        Task<List<OtModelProfile>> GetAllEnabledAsync();

        Task<bool> AddAsync(OtModelProfile profile);

        Task<bool> UpdateAsync(OtModelProfile profile);
    }
}
