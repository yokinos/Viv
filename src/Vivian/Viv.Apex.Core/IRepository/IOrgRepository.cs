using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>
    /// 组织仓储接口
    /// 缓存 Key: OrgId，一次缓存 Org + 它的 AppRelation 列表
    /// </summary>
    public interface IOrgRepository
    {
        /// <summary>
        /// 新增组织
        /// </summary>
        Task<bool> AddAsync(AtOrg org);

        /// <summary>
        /// 更新组织
        /// </summary>
        Task<bool> UpdateAsync(AtOrg org);

        /// <summary>
        /// 物理删除组织
        /// </summary>
        Task<bool> DeleteAsync(long orgId);

        /// <summary>
        /// 软删除组织
        /// </summary>
        Task<bool> SoftDeleteAsync(long orgId);

        /// <summary>
        /// 根据Id获取组织及其App权限列表（缓存优先）
        /// </summary>
        Task<(AtOrg? Org, List<AtOrgAppRelation>? Relations)> GetAsync(long orgId);

        /// <summary>
        /// 根据Id获取组织及其App权限列表（缓存优先）
        /// </summary>
        Task<(AtOrg? Org, List<AtOrgAppRelation>? Relations)> GetOrgByOrgCodeAsync(string orgCode);

        /// <summary>
        /// 获取子组织列表
        /// </summary>
        Task<List<AtOrg>> GetChildrenAsync(long parentId);

        /// <summary>
        /// 分页查询组织
        /// </summary>
        Task<PagedList<AtOrg>> GetPagedListAsync(IApiPagedRequest request);

        /// <summary>
        /// 新增组织应用关联
        /// </summary>
        Task<bool> AddRelationAsync(AtOrgAppRelation relation);

        /// <summary>
        /// 更新组织应用关联
        /// </summary>
        Task<bool> UpdateRelationAsync(AtOrgAppRelation relation);

        /// <summary>
        /// 物理删除组织应用关联（按 OrgId + ClientAppId）
        /// </summary>
        Task<bool> DeleteRelationAsync(long orgId, long clientAppId);

        /// <summary>
        /// 软删除组织应用关联（按 OrgId + ClientAppId）
        /// </summary>
        Task<bool> SoftDeleteRelationAsync(long orgId, long clientAppId);

        /// <summary>
        /// 获取指定组织的所有应用关联
        /// </summary>
        Task<List<AtOrgAppRelation>> GetRelationsByOrgAsync(long orgId);

        /// <summary>
        /// 分页查询组织应用关联
        /// </summary>
        Task<PagedList<AtOrgAppRelation>> GetPagedRelationListAsync(IApiPagedRequest request);
    }
}
