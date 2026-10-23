using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>菜单子页面仓储</summary>
    public interface IMenuSubPageRepository
    {
        /// <summary>按菜单 Id 批量取启用的子页面</summary>
        Task<List<AtMenuSubPage>> GetByMenuIdsAsync(IReadOnlyList<long> menuIds);

        /// <summary>菜单删除时把它的子页面一并软删</summary>
        Task<bool> SoftDeleteByMenuAsync(long menuId);
    }
}
