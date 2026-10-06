using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Entity.Database.Ouroboros;
using Viv.Momo;
using Viv.Ouroboros.Core.IRepository;

namespace Viv.Ouroboros.Core.Repository
{
    /// <summary>
    /// 模型档位仓储。纯库表读写，缓存交给 IModelProfileProvider。
    /// </summary>
    public class ModelProfileRepository : IModelProfileRepository
    {
        private readonly IMomoDbContext _dbContext;

        public ModelProfileRepository(IMomoDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<OtModelProfile>> GetEnabledByKeyAsync(string profileKey)
        {
            var rows = await _dbContext.FindListAsync<OtModelProfile>(x => x.ProfileKey == profileKey && x.IsEnabled);
            return rows.OrderBy(x => x.Priority).ToList();
        }

        public async Task<List<OtModelProfile>> GetAllEnabledAsync()
        {
            var rows = await _dbContext.FindListAsync<OtModelProfile>(x => x.IsEnabled);
            return rows.OrderBy(x => x.ProfileKey).ThenBy(x => x.Priority).ToList();
        }

        public async Task<bool> AddAsync(OtModelProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            profile.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(profile);
        }

        public async Task<bool> UpdateAsync(OtModelProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            profile.UpdatedAt = DateTime.Now;
            return await _dbContext.UpdateAsync(profile);
        }
    }
}
