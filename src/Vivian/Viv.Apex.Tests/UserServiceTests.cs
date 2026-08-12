using System.Collections.Generic;
using System.Threading.Tasks;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.Service;
using Viv.Delusion;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    public class UserServiceTests
    {
        private static ApexLoginRequest CreateLoginRequest() => new()
        {
            AppId = 7,
            Version = 1000,
            UserName = "13800138000",
            Password = "pwd123",
            UserType = EmUserType.Master
        };

        [Fact]
        public async Task LoginAsync_UnknownUserType_ReturnsFailed()
        {
            var service = new UserService(new FakeLoginIndex(new Dictionary<EmUserType, ILoginContract>()));

            var result = await service.LoginAsync(CreateLoginRequest());

            Assert.False(result.Code >= 200);
            Assert.Equal("未知的用户类型", result.Message);
        }

        [Fact]
        public async Task LoginAsync_ImplFails_ReturnsFailedWithImplMessage()
        {
            var impl = new StubLoginContract
            {
                LoginResult = FuncResult<ApexLoginOutput>.Failed("登录失败")
            };
            var index = new FakeLoginIndex(new Dictionary<EmUserType, ILoginContract>
            {
                [EmUserType.Master] = impl
            });
            var service = new UserService(index);

            var result = await service.LoginAsync(CreateLoginRequest());

            Assert.False(result.Code >= 200);
            Assert.Equal("登录失败", result.Message);
        }

        [Fact]
        public async Task LoginAsync_ImplSucceeds_ReturnsSuccessWithData()
        {
            var output = new ApexLoginOutput { UserId = 42, AccessToken = "access-token" };
            var impl = new StubLoginContract
            {
                LoginResult = FuncResult<ApexLoginOutput>.Success("login success", output)
            };
            var index = new FakeLoginIndex(new Dictionary<EmUserType, ILoginContract>
            {
                [EmUserType.Master] = impl
            });
            var service = new UserService(index);

            var result = await service.LoginAsync(CreateLoginRequest());

            Assert.True(result.Code >= 200);
            Assert.Equal(42, result.Data!.UserId);
            Assert.Equal("access-token", result.Data.AccessToken);
        }
    }
}
