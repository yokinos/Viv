using Viv.Apex.Core.Entity.Dto.User;
using Viv.Apex.Core.Entity.Vo.User;
using Viv.Apex.Core.IService;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class UserService : IUserService
    {
        private readonly IMenuService _menu;

        public UserService(IMenuService menu)
        {
            _menu = menu;
        }

        public async Task<VivApiResult> GetLoginDataAsync(ApiEmptyRequest request)
        {
            var output = new GetLoginDataOutput();

            // 菜单跟着登录一次下发，省掉登录后再拉一次；取不到就留空，不挡登录
            var routes = await _menu.GetRouteTreeAsync();
            if (routes is VivApiResult<object> typed)
                output.Routes = typed.Data;

            return VivApiResult.Success(output);
        }

        public async Task<VivApiResult> GetUserAsync(GetUserRequest request)
        {
            return VivApiResult.Success();
        }
    }
}
