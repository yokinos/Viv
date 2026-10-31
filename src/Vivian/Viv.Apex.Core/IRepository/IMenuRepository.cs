using Viv.Apex.Core.Entity.Vo.App;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>菜单仓储。排序交给上层（菜单量小，SQL 排序不值得单开接口）</summary>
    public interface IMenuRepository
    {
        /// <summary>取该应用下启用的菜单节点（软删除由全局过滤器挡掉）</summary>
        Task<List<AtMenu>> GetEnabledByAppAsync(long clientAppId);

        /// <summary>取该应用下全部未删除菜单，含停用。后台维护用</summary>
        Task<List<AtMenu>> GetByAppAsync(long clientAppId);

        /// <summary>后台维护分页。SQL 由 <see cref="IApiPagedRequest.GetSqlQuery"/> 给出，行上带父级名称与下级数量</summary>
        Task<PagedList<AppMenuRow>> GetPagedAsync(IApiPagedRequest request);

        /// <summary>该应用下一个空闲的权限位。没有菜单时从 0 开始</summary>
        Task<int> NextBitIndexAsync(long clientAppId);

        /// <summary>按 Id 取，不存在或已删返回 null</summary>
        Task<AtMenu?> GetAsync(long id);

        /// <summary>按 Id 新增，返回落库后的行Id</summary>
        Task<long> AddAsync(AtMenu menu);

        /// <summary>按 Id 更新</summary>
        Task<bool> UpdateAsync(AtMenu menu);

        /// <summary>软删除一行</summary>
        Task<bool> SoftDeleteAsync(long id);

        /// <summary>有没有子节点（删除前挡一道）</summary>
        Task<bool> HasChildrenAsync(long parentId);
    }
}
