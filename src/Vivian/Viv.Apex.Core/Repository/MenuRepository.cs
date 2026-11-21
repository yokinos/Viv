using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
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

        public Task<PagedList<AppMenuRow>> GetPagedAsync(IApiPagedRequest request)
        {
            var (sql, parameters) = request.GetSqlQuery();
            return _dbContext.PageAsync<AppMenuRow>(sql, request.PageIndex, request.PageSize, parameters);
        }

        /// <summary>
        /// 该应用下的下一个菜单位。取<b>含软删行</b>的 MAX：位号一经分配不回收，
        /// 否则删掉高位菜单后新菜单复用同一个位，旧角色掩码会静默变成对新菜单的授权
        /// （EF 的全局软删过滤器会把已删行滤掉，所以走裸 SQL）。
        /// </summary>
        public Task<int> NextBitIndexAsync(long clientAppId)
            => _dbContext.FindScalarAsync<int>(
                "SELECT COALESCE(MAX(BitIndex), -1) + 1 FROM AtMenu WHERE ClientAppId = @clientAppId",
                new { clientAppId });

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
