using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Elysia.CacheBucket;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class OrgRepository : DataAccessCacheBase<EntityListBucket<AtOrg, AtOrgAppRelation>>, IOrgRepository
    {
        public OrgRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        public async Task<bool> AddAsync(AtOrg org)
        {
            var flag = await _dbContext.InsertAsync(org);
            if (flag)
            {
                await RefreshAsync(org.Id);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtOrg org)
        {
            var flag = await _dbContext.UpdateAsync(org);
            if (flag)
            {
                await RefreshAsync(org.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long orgId)
        {
            var flag = await _dbContext.DeleteAsync<AtOrg>(x => x.Id == orgId);
            if (flag)
            {
                await RefreshAsync(orgId);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteAsync(long orgId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtOrg>(x => x.Id == orgId);
            if (flag)
            {
                await RefreshAsync(orgId);
            }
            return flag;
        }

        public async Task<(AtOrg? Org, List<AtOrgAppRelation>? Relations)> GetAsync(long orgId)
        {
            var bucket = await GetCacheAsync(orgId);
            return (bucket?.Entity, bucket?.Entities);
        }

        public override async Task<EntityListBucket<AtOrg, AtOrgAppRelation>?> GetDbAsync(params object[] keys)
        {
            var orgId = keys[0].As<long>();
            var org = await _dbContext.SingleOrDefaultAsync<AtOrg>(x => x.Id == orgId && !x.IsDeleted);
            if (org == null) return null;

            var relations = await _dbContext.FindListAsync<AtOrgAppRelation>(x => x.OrgId == orgId && !x.IsDeleted);

            return new EntityListBucket<AtOrg, AtOrgAppRelation>
            {
                Entity = org,
                Entities = relations
            };
        }

        public async Task<List<AtOrg>> GetChildrenAsync(long parentId)
        {
            return await _dbContext.FindListAsync<AtOrg>(x => x.ParentId == parentId && !x.IsDeleted);
        }

        public async Task<PagedList<AtOrg>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtOrg>(sql, request.PageIndex, request.PageSize, parameter);
        }

        // ==================== AtOrgAppRelation ====================

        public async Task<bool> AddRelationAsync(AtOrgAppRelation relation)
        {
            var flag = await _dbContext.InsertAsync(relation);
            if (flag)
            {
                await RefreshAsync(relation.OrgId);
            }
            return flag;
        }

        public async Task<bool> UpdateRelationAsync(AtOrgAppRelation relation)
        {
            var flag = await _dbContext.UpdateAsync(relation);
            if (flag)
            {
                await RefreshAsync(relation.OrgId);
            }
            return flag;
        }

        public async Task<bool> DeleteRelationAsync(long orgId, long clientAppId)
        {
            var flag = await _dbContext.DeleteAsync<AtOrgAppRelation>(x => x.OrgId == orgId && x.ClientAppId == clientAppId);
            if (flag)
            {
                await RefreshAsync(orgId);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteRelationAsync(long orgId, long clientAppId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtOrgAppRelation>(x => x.OrgId == orgId && x.ClientAppId == clientAppId);
            if (flag)
            {
                await RefreshAsync(orgId);
            }
            return flag;
        }

        public async Task<List<AtOrgAppRelation>> GetRelationsByOrgAsync(long orgId)
        {
            return await _dbContext.FindListAsync<AtOrgAppRelation>(x => x.OrgId == orgId && !x.IsDeleted);
        }

        public async Task<PagedList<AtOrgAppRelation>> GetPagedRelationListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtOrgAppRelation>(sql, request.PageIndex, request.PageSize, parameter);
        }
    }
}
