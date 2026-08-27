using System;
using System.Numerics;
using System.Threading.Tasks;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Elysia;
using Viv.Entity.Any;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Impl.Login
{
    [VivDependency(EmUserType.Master)]
    public class MasterUserLoginImpl : LoginImplBase, ILoginContract, IDependency
    {
        public MasterUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository, IClientAppRepository clientAppRepository)
            : base(tokenService, redisService, context, userRepository, clientAppRepository)
        {

        }

        public async Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
            if (request.UserType != EmUserType.Master)
            {
                return FuncResult<LoginOutput>.Failed("登录类型非法");
            }

            var validateApp = await ValidateAppAsync(request.AppId);
            if (!validateApp.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateApp.Message);
            }

            var validateUser = await ValidateUserAsync(request.UserType, request.UserName, request.Password);
            if (!validateUser.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateUser.Message);
            }

            var output = await BuildLoginOutputAsync(request.AppId, validateUser.Data);
            return FuncResult<LoginOutput>.Success("login success", output);
        }
    }
}