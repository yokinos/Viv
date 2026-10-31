using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Apex;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Apex.Core.Repository
{
    /// <summary>菜单动作（权限点）仓储</summary>
    public class MenuActionRepository : DataAccessBase, IMenuActionRepository
    {
        public MenuActionRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public Task<List<AtMenuAction>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds)
        {
            if (menuIds.Count == 0) return Task.FromResult(new List<AtMenuAction>());

            var ids = menuIds.ToList();
            return _dbContext.FindListAsync<AtMenuAction>(x => ids.Contains(x.MenuId));
        }

        public Task<AtMenuAction?> GetAsync(long id)
            => _dbContext.SingleOrDefaultAsync<AtMenuAction>(x => x.Id == id);

        public async Task<int> NextBitIndexAsync(long menuId)
        {
            var actions = await _dbContext.FindListAsync<AtMenuAction>(x => x.MenuId == menuId).ConfigureAwait(false);
            return actions.Count == 0 ? 0 : actions.Max(x => x.BitIndex) + 1;
        }

        public async Task<long> AddAsync(AtMenuAction action)
        {
            await _dbContext.InsertAsync(action).ConfigureAwait(false);
            return action.Id;
        }

        public Task<bool> UpdateAsync(AtMenuAction action)
            => _dbContext.UpdateAsync(action);

        public Task<bool> SoftDeleteAsync(long id)
            => _dbContext.SoftDeleteAsync<AtMenuAction>(id);

        public Task<bool> SoftDeleteByMenuAsync(long menuId)
            => _dbContext.SoftDeleteAsync<AtMenuAction>(x => x.MenuId == menuId);
    }
}
