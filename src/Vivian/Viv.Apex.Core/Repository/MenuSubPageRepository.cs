using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Apex.Core.Repository
{
    /// <summary>菜单子页面仓储</summary>
    public class MenuSubPageRepository : DataAccessBase, IMenuSubPageRepository
    {
        public MenuSubPageRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public Task<List<AtMenuSubPage>> GetByMenuIdsAsync(IReadOnlyList<long> menuIds)
        {
            if (menuIds.Count == 0) return Task.FromResult(new List<AtMenuSubPage>());

            var ids = menuIds.ToList();
            return _dbContext.FindListAsync<AtMenuSubPage>(
                x => ids.Contains(x.MenuId) && x.Status == EmStatus.Enabled);
        }

        public Task<List<AtMenuSubPage>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds)
        {
            if (menuIds.Count == 0) return Task.FromResult(new List<AtMenuSubPage>());

            var ids = menuIds.ToList();
            return _dbContext.FindListAsync<AtMenuSubPage>(x => ids.Contains(x.MenuId));
        }

        public Task<AtMenuSubPage?> GetAsync(long id)
            => _dbContext.SingleOrDefaultAsync<AtMenuSubPage>(x => x.Id == id);

        public async Task<int> NextBitIndexAsync(long menuId)
        {
            var pages = await _dbContext.FindListAsync<AtMenuSubPage>(x => x.MenuId == menuId).ConfigureAwait(false);
            return pages.Count == 0 ? 0 : pages.Max(x => x.BitIndex) + 1;
        }

        public async Task<long> AddAsync(AtMenuSubPage page)
        {
            await _dbContext.InsertAsync(page).ConfigureAwait(false);
            return page.Id;
        }

        public Task<bool> UpdateAsync(AtMenuSubPage page)
            => _dbContext.UpdateAsync(page);

        public Task<bool> SoftDeleteAsync(long id)
            => _dbContext.SoftDeleteAsync<AtMenuSubPage>(id);

        public Task<bool> SoftDeleteByMenuAsync(long menuId)
            => _dbContext.SoftDeleteAsync<AtMenuSubPage>(x => x.MenuId == menuId);
    }
}
