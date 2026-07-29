using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>
    /// 客户端应用配置仓储接口
    /// 缓存 Key: ClientAppId + ConfigKey
    /// </summary>
    public interface IClientAppSettingRepository
    {
        /// <summary>
        /// 新增配置
        /// </summary>
        Task<bool> AddAsync(AtClientAppSetting setting);

        /// <summary>
        /// 更新配置
        /// </summary>
        Task<bool> UpdateAsync(AtClientAppSetting setting);

        /// <summary>
        /// 物理删除配置（按 ClientAppId + ConfigKey）
        /// </summary>
        Task<bool> DeleteAsync(long clientAppId, string configKey);

        /// <summary>
        /// 软删除配置（按 ClientAppId + ConfigKey）
        /// </summary>
        Task<bool> SoftDeleteAsync(long clientAppId, string configKey);

        /// <summary>
        /// 根据 ClientAppId + ConfigKey 获取配置（缓存优先）
        /// </summary>
        Task<AtClientAppSetting?> GetAsync(long clientAppId, string configKey);

        /// <summary>
        /// 获取指定应用的所有配置
        /// </summary>
        Task<List<AtClientAppSetting>> GetSettingsByAppAsync(long clientAppId);

        /// <summary>
        /// 分页查询配置
        /// </summary>
        Task<PagedList<AtClientAppSetting>> GetPagedListAsync(IApiPagedRequest request);
    }
}
