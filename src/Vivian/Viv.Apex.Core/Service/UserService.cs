using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Contracts.Interface;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAiClientFactory _aiClientFactory;

        public UserService(IUserRepository userRepository, IAiClientFactory aiClientFactory)
        {
            _userRepository = userRepository;
            _aiClientFactory = aiClientFactory;
        }

        public async Task<VivApiResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            return VivApiResult<ApexLoginOutput>.Success("Login successful");
        }
    }
}
