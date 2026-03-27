using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.ViewModel.Account.Request;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<VivApiResult> LoginAsync(ApexLoginRequest request)
        {
            return VivApiResult.ApiRsult(ResultCode.Success, "Login successful");
        }
    }
}
