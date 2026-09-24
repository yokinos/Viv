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
        Task<bool> AddUserAsync(AtUser user);

        /// <summary>
        /// 更新用户
        /// </summary>
        Task<bool> UpdateUserAsync(AtUser user);

        /// <summary>
        /// 物理删除用户
        /// </summary>
        Task<bool> DeleteUserAsync(long userId);

        /// <summary>
        /// 软删除用户
        /// </summary>
        Task<bool> SoftDeleteUserAsync(long userId);

        /// <summary>
        /// 根据Id获取用户
        /// </summary>
        Task<AtUser?> GetUserAsync(long userId);

        /// <summary>
        /// 根据手机号 + 用户类型获取用户
        /// </summary>
        Task<AtUser?> GetUserByPhoneAsync(string phone, EmUserType userType);

        /// <summary>
        /// 分页查询用户
        /// </summary>
        Task<PagedList<AtUser>> GetUserPagedListAsync(IApiPagedRequest request);

        /// <summary>
        /// 取用户的缓存桶
        /// </summary>
        Task<UserBucket?> GetUserBucketAsync(long userId);

        /// <summary>
        /// 取用户绑定的角色列表
        /// </summary>
        Task<List<AtUserRole>> GetUserRolesAsync(long userId);

        /// <summary>
        /// 新增角色
        /// </summary>
        Task<bool> AddRoleAsync(AtUserRole role);

        /// <summary>
        /// 更新角色
        /// </summary>
        Task<bool> UpdateRoleAsync(AtUserRole role);

        /// <summary>
        /// 物理删除角色
        /// </summary>
        Task<bool> DeleteRoleAsync(long roleId);

        /// <summary>
        /// 软删除角色
        /// </summary>
        Task<bool> SoftDeleteRoleAsync(long roleId);

        /// <summary>
        /// 根据Id获取角色
        /// </summary>
        Task<AtUserRole?> GetRoleAsync(long roleId);

        /// <summary>
        /// 分页查询角色
        /// </summary>
        Task<PagedList<AtUserRole>> GetRolePagedListAsync(IApiPagedRequest request);
    }
}
