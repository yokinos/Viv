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
        /// 该应用下的下一个空闲权限位。**按应用分组**，不是全局 —— 角色的操作掩码按
        /// 「角色 × 应用」存（<c>AtUserRoleApp.ButtonMask</c>），跨应用的同一位不会相遇。
        /// 位号一经分配不回收（取含软删行的 MAX），否则旧掩码会静默授权给新动作。
        /// </summary>
        Task<int> NextBitIndexAsync(long clientAppId);

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
