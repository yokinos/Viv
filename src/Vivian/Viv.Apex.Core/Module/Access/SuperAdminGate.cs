using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Module.Access
{
    /// <summary>
    /// 应用与菜单的后台维护只放给超级管理员。登录上下文里没有这个标记，要回用户表看。
    /// </summary>
    public static class SuperAdminGate
    {
        public static async Task<VivApiResult?> DenyAsync(IVivContext context, IUserRepository users)
        {
            if (context.UserId <= 0)
                return VivApiResult.Failed("请先登录");

            var user = await users.GetUserAsync(context.UserId);
            if (user is null || user.Status != EmStatus.Enabled || !user.IsSuperAdmin)
                return VivApiResult.Failed("只有超级管理员可以管理应用");

            return null;
        }
    }
}
