using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    /// <summary>菜单动作（权限点）仓储。排序交给上层（一个页面下的动作很少）</summary>
    public interface IMenuActionRepository
    {
        /// <summary>取指定菜单下未删除的动作（含停用），软删除由全局过滤器挡掉</summary>
        Task<List<AtMenuAction>> ListByMenuIdsAsync(IReadOnlyList<long> menuIds);

        /// <summary>按 Id 取，不存在或已删返回 null</summary>
        Task<AtMenuAction?> GetAsync(long id);

        /// <summary>
        /// 下一个空闲的权限位。**全局唯一**，不是每个菜单从 0 开始 —— 角色的授权掩码
        /// （<c>AtUserRole.ButtonMask</c>）没有菜单维度，位重复就等于互相授权。
        /// </summary>
        Task<int> NextBitIndexAsync();

        /// <summary>按 Id 新增，返回落库后的行 Id</summary>
        Task<long> AddAsync(AtMenuAction action);

        /// <summary>按 Id 更新</summary>
        Task<bool> UpdateAsync(AtMenuAction action);

        /// <summary>软删除一行</summary>
        Task<bool> SoftDeleteAsync(long id);

        /// <summary>软删除某个菜单下的全部动作（删菜单时级联）</summary>
        Task<bool> SoftDeleteByMenuAsync(long menuId);
    }
}
