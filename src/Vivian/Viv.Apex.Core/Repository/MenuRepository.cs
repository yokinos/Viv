using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Apex.Core.Repository
{
    /// <summary>菜单仓储</summary>
    public class MenuRepository : DataAccessBase, IMenuRepository
    {
        public MenuRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public Task<List<AtMenu>> GetEnabledByAppAsync(long clientAppId)
            => _dbContext.FindListAsync<AtMenu>(x => x.ClientAppId == clientAppId && x.Status == EmStatus.Enabled);

        public Task<List<AtMenu>> GetByAppAsync(long clientAppId)
            => _dbContext.FindListAsync<AtMenu>(x => x.ClientAppId == clientAppId);

        public async Task<int> NextBitIndexAsync(long clientAppId)
        {
            var menus = await GetByAppAsync(clientAppId);
            return menus.Count == 0 ? 0 : menus.Max(x => x.BitIndex) + 1;
        }

        public Task<AtMenu?> GetAsync(long id)
            => _dbContext.SingleOrDefaultAsync<AtMenu>(x => x.Id == id);

        public async Task<long> AddAsync(AtMenu menu)
        {
            await _dbContext.InsertAsync(menu).ConfigureAwait(false);
            return menu.Id;
        }

        public Task<bool> UpdateAsync(AtMenu menu)
            => _dbContext.UpdateAsync(menu);

        public Task<bool> SoftDeleteAsync(long id)
            => _dbContext.SoftDeleteAsync<AtMenu>(id);

        public Task<bool> HasChildrenAsync(long parentId)
            => _dbContext.ExistAsync<AtMenu>(x => x.ParentId == parentId);
    }
}
