using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>
    /// 客户端轮播仓储接口
    /// 缓存 Key: ClientAppId + Position
    /// </summary>
    public interface IClientAppCarouselRepository
    {
        /// <summary>
        /// 新增轮播
        /// </summary>
        Task<bool> AddAsync(AtClientAppCarousel carousel);

        /// <summary>
        /// 更新轮播
        /// </summary>
        Task<bool> UpdateAsync(AtClientAppCarousel carousel);

        /// <summary>
        /// 物理删除轮播（按 ClientAppId + Position）
        /// </summary>
        Task<bool> DeleteAsync(long clientAppId, byte position);

        /// <summary>
        /// 软删除轮播（按 ClientAppId + Position）
        /// </summary>
        Task<bool> SoftDeleteAsync(long clientAppId, byte position);

        /// <summary>
        /// 根据 ClientAppId + Position 获取轮播（缓存优先）
        /// </summary>
        Task<AtClientAppCarousel?> GetAsync(long clientAppId, byte position);

        /// <summary>
        /// 获取指定应用的轮播列表
        /// </summary>
        Task<List<AtClientAppCarousel>> GetCarouselsByAppAsync(long clientAppId);

        /// <summary>
        /// 分页查询轮播
        /// </summary>
        Task<PagedList<AtClientAppCarousel>> GetPagedListAsync(IApiPagedRequest request);
    }
}
