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
        Task<bool> AddAppAsync(AtClientApp app);

        /// <summary>
        /// 更新客户端应用
        /// </summary>
        Task<bool> UpdateAppAsync(AtClientApp app);

        /// <summary>丢掉 AppKey → 主键的缓存。改 Key 时旧 Key 还要单独清一次。</summary>
        Task ForgetAppKeyAsync(string? appKey);

        /// <summary>
        /// 物理删除客户端应用
        /// </summary>
        Task<bool> DeleteAppAsync(long appId);

        /// <summary>
        /// 软删除客户端应用
        /// </summary>
        Task<bool> SoftDeleteAppAsync(long appId);

        /// <summary>
        /// 根据Id获取客户端应用
        /// </summary>
        Task<AtClientApp?> GetAppAsync(long appId);

        /// <summary>
        /// 按对外 AppKey 取客户端应用（登录入口用）。
        /// 内部先解析成主键再走实体缓存，返回的实体带 Id，调用方后续一律用 Id。
        /// </summary>
        Task<AtClientApp?> GetAppByAppKeyAsync(string appKey);

        /// <summary>
        /// 分页查询客户端应用
        /// </summary>
        Task<PagedList<AtClientApp>> GetAppPagedListAsync(IApiPagedRequest request);

        /// <summary>
        /// 分页查询客户端应用（按参数，给 gRPC 这类内部调用用）。
        /// 与 <see cref="GetAppPagedListAsync"/> 的区别：不依赖 HTTP 的 <see cref="IApiPagedRequest"/>，
        /// 分页参数由调用方显式给，仓储负责收敛与 SQL 拼装。
        /// </summary>
        Task<PagedList<AtClientApp>> GetAppPageAsync(
            int pageIndex,
            int pageSize,
            string? keyword = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 全部未删除的客户端应用。数量小，筛选和分页放在服务里做。
        /// </summary>
        Task<List<AtClientApp>> ListAppsAsync();

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
