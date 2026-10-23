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
    }
}
