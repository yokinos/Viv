using Viv.Apex.Core.Entity.CacheBucket;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>
    /// 用户仓储接口
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>
        /// 新增用户
        /// </summary>
        Task<bool> AddAsync(AtUser user);

        /// <summary>
        /// 更新用户
        /// </summary>
        Task<bool> UpdateAsync(AtUser user);

        /// <summary>
        /// 物理删除用户
        /// </summary>
        Task<bool> DeleteAsync(long userId);

        /// <summary>
        /// 软删除用户
        /// </summary>
        Task<bool> SoftDeleteAsync(long userId);

        /// <summary>
        /// 根据Id获取用户
        /// </summary>
        Task<AtUser?> GetAsync(long userId);

        /// <summary>
        /// 根据手机号 + 用户类型获取用户
        /// </summary>
        Task<AtUser?> GetByPhoneAsync(string phone, EmUserType userType);

        /// <summary>
        /// 分页查询用户
        /// </summary>
        Task<PagedList<AtUser>> GetPagedListAsync(IApiPagedRequest request);

        Task<AtUserBucket?> GetUserBucketAsync(long userId);

        Task<List<AtUserRole>> GetAtUserRoleListAsync(long userId);
    }
}
