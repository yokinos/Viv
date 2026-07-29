using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>
    /// 客户端应用仓储接口（聚合根：AtClientApp + 子实体）
    /// </summary>
    public interface IClientAppRepository
    {
        // ==================== AtClientApp ====================

        /// <summary>
        /// 新增客户端应用
        /// </summary>
        Task<bool> AddAsync(AtClientApp app);

        /// <summary>
        /// 更新客户端应用
        /// </summary>
        Task<bool> UpdateAsync(AtClientApp app);

        /// <summary>
        /// 物理删除客户端应用
        /// </summary>
        Task<bool> DeleteAsync(long appId);

        /// <summary>
        /// 软删除客户端应用
        /// </summary>
        Task<bool> SoftDeleteAsync(long appId);

        /// <summary>
        /// 根据Id获取客户端应用
        /// </summary>
        Task<AtClientApp?> GetAsync(long appId);

        /// <summary>
        /// 分页查询客户端应用
        /// </summary>
        Task<PagedList<AtClientApp>> GetPagedListAsync(IApiPagedRequest request);

        // ==================== AtClientAppVersion ====================

        /// <summary>
        /// 新增版本
        /// </summary>
        Task<bool> AddVersionAsync(AtClientAppVersion version);

        /// <summary>
        /// 更新版本
        /// </summary>
        Task<bool> UpdateVersionAsync(AtClientAppVersion version);

        /// <summary>
        /// 物理删除版本
        /// </summary>
        Task<bool> DeleteVersionAsync(long versionId);

        /// <summary>
        /// 软删除版本
        /// </summary>
        Task<bool> SoftDeleteVersionAsync(long versionId);

        /// <summary>
        /// 根据Id获取版本
        /// </summary>
        Task<AtClientAppVersion?> GetVersionAsync(long versionId);

        /// <summary>
        /// 获取指定应用的所有版本
        /// </summary>
        Task<List<AtClientAppVersion>> GetVersionsByAppAsync(long clientAppId);

        /// <summary>
        /// 分页查询版本
        /// </summary>
        Task<PagedList<AtClientAppVersion>> GetPagedVersionListAsync(IApiPagedRequest request);

        // ==================== AtClientAppNotice ====================

        /// <summary>
        /// 新增公告
        /// </summary>
        Task<bool> AddNoticeAsync(AtClientAppNotice notice);

        /// <summary>
        /// 更新公告
        /// </summary>
        Task<bool> UpdateNoticeAsync(AtClientAppNotice notice);

        /// <summary>
        /// 物理删除公告
        /// </summary>
        Task<bool> DeleteNoticeAsync(long noticeId);

        /// <summary>
        /// 软删除公告
        /// </summary>
        Task<bool> SoftDeleteNoticeAsync(long noticeId);

        /// <summary>
        /// 根据Id获取公告
        /// </summary>
        Task<AtClientAppNotice?> GetNoticeAsync(long noticeId);

        /// <summary>
        /// 获取指定应用的公告列表
        /// </summary>
        Task<List<AtClientAppNotice>> GetNoticesByAppAsync(long clientAppId);

        /// <summary>
        /// 分页查询公告
        /// </summary>
        Task<PagedList<AtClientAppNotice>> GetPagedNoticeListAsync(IApiPagedRequest request);
    }
}
