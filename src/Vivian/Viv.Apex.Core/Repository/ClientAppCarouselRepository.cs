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
    public class ClientAppCarouselRepository : DataAccessCacheBase<EntityListBucket<AtClientAppCarousel>>, IClientAppCarouselRepository
    {
        public ClientAppCarouselRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        public async Task<bool> AddAsync(AtClientAppCarousel carousel)
        {
            var flag = await _dbContext.InsertAsync(carousel);
            if (flag)
            {
                await RefreshAsync(carousel.ClientAppId, carousel.Position);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtClientAppCarousel carousel)
        {
            var flag = await _dbContext.UpdateAsync(carousel);
            if (flag)
            {
                await RefreshAsync(carousel.ClientAppId, carousel.Position);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long clientAppId, byte position)
        {
            var flag = await _dbContext.DeleteAsync<AtClientAppCarousel>(x => x.ClientAppId == clientAppId && x.Position == position);
            if (flag)
            {
                await RefreshAsync(clientAppId, position);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteAsync(long clientAppId, byte position)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtClientAppCarousel>(x => x.ClientAppId == clientAppId && x.Position == position);
            if (flag)
            {
                await RefreshAsync(clientAppId, position);
            }
            return flag;
        }

        public async Task<AtClientAppCarousel?> GetAsync(long clientAppId, byte position)
        {
            var bucket = await GetCacheAsync(clientAppId, position);
            return bucket?.Entity;
        }

        public override async Task<EntityListBucket<AtClientAppCarousel>?> GetDbAsync(params object[] keys)
        {
            var clientAppId = keys[0].As<long>();
            var position = keys[1].As<byte>();
            var carousel = await _dbContext.SingleOrDefaultAsync<AtClientAppCarousel>(
                x => x.ClientAppId == clientAppId && x.Position == position && !x.IsDeleted);
            if (carousel == null) return null;
            return new EntityListBucket<AtClientAppCarousel>(carousel);
        }

        public async Task<List<AtClientAppCarousel>> GetCarouselsByAppAsync(long clientAppId)
        {
            return await _dbContext.FindListAsync<AtClientAppCarousel>(x => x.ClientAppId == clientAppId && !x.IsDeleted);
        }

        public async Task<PagedList<AtClientAppCarousel>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtClientAppCarousel>(sql, request.PageIndex, request.PageSize, parameter);
        }
    }
}
