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

        /// <summary>
        /// 该应用下的下一个动作位。<b>按应用分组</b>（角色的操作掩码按「角色 × 应用」存，
        /// 见 <see cref="AtUserRoleApp.ButtonMask"/>）；动作表没有 ClientAppId 列，归属靠 MenuId 反查。
        ///
        /// <para>走裸 SQL 而不是 <c>FindListAsync</c>：发号要取<b>含软删行</b>的 MAX，否则删掉高位动作后
        /// 新动作复用同一个位，旧角色掩码会静默变成对新动作的授权（EF 的全局软删过滤器会把这些行滤掉）。
        /// COALESCE 是为了两种方言都能跑。</para>
        /// </summary>
        public async Task<int> NextBitIndexAsync(long clientAppId)
        {
            var next = await _dbContext.FindScalarAsync<int>(
                @"SELECT COALESCE(MAX(a.BitIndex), -1) + 1
                  FROM AtMenuAction a INNER JOIN AtMenu m ON m.Id = a.MenuId
                  WHERE m.ClientAppId = @clientAppId",
                new { clientAppId }).ConfigureAwait(false);

            return next;
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
