using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>菜单子页面仓储</summary>
    public interface IMenuSubPageRepository
    {
        /// <summary>按菜单 Id 批量取启用的子页面</summary>
        Task<List<AtMenuSubPage>> GetByMenuIdsAsync(IReadOnlyList<long> menuIds);

        /// <summary>后台维护用：含停用的子页面</summary>
        Task<List<AtMenuSubPage>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds);

        /// <summary>按 Id 取，不存在或已删返回 null</summary>
        Task<AtMenuSubPage?> GetAsync(long id);

        /// <summary>该菜单下一个空闲的权限位。没有子页面时从 0 开始</summary>
        Task<int> NextBitIndexAsync(long menuId);

        /// <summary>按 Id 新增，返回落库后的行 Id</summary>
        Task<long> AddAsync(AtMenuSubPage page);

        /// <summary>按 Id 更新</summary>
        Task<bool> UpdateAsync(AtMenuSubPage page);

        /// <summary>软删除一行</summary>
        Task<bool> SoftDeleteAsync(long id);

        /// <summary>菜单删除时把它的子页面一并软删</summary>
        Task<bool> SoftDeleteByMenuAsync(long menuId);
    }
}
