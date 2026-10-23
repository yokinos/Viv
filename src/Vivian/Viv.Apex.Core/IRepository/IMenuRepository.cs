using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>菜单仓储。按客户端应用取，排序交给上层（菜单量小，SQL 排序不值得单开接口）</summary>
    public interface IMenuRepository
    {
        /// <summary>取该应用下启用的菜单节点（软删除由全局过滤器挡掉）</summary>
        Task<List<AtMenu>> GetEnabledByAppAsync(long clientAppId);
    }
}
