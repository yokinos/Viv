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
    }
}
