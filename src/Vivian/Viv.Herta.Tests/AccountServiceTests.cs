using Viv.Entity.Database.Herta;
using Viv.Fakes;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Service;

namespace Viv.Herta.Tests
{
    /// <summary>登录前的客户端校验：AppId 会写进 token，不能等连接准入再查。</summary>
    public class AccountServiceTests
    {
        private const long SubjectId = 1;
        private const long AppId = 7;

        private static (AccountService Service, StubClientAppQuery ClientApps, StubChatUserRepository Users) Create()
        {
            var users = new StubChatUserRepository();
            var hasher = new PasswordHasher();
            var (hash, salt) = hasher.Hash("secret123");

            users.Users.Add(new EtChatUser
            {
                Id = 100,
                TenantId = SubjectId,
                LoginCode = "alice",
                Password = hash,
                Salt = salt,
                NickName = "Alice"
            });

            var clientApps = new StubClientAppQuery();
            var service = new AccountService(users, hasher, new StubTokenService(), clientApps);
            return (service, clientApps, users);
        }

        private static HertaLoginRequest LoginRequest() => new()
        {
            SubjectId = SubjectId,
            AppId = AppId,
            LoginCode = "alice",
            Password = "secret123"
        };

        [Fact]
        public async Task HertaLogin_Apex不认识这个AppId_拒绝且不签发token()
        {
            var (service, _, _) = Create();

            var result = await service.HertaLoginAsync(LoginRequest());

            Assert.False(result.Code >= 200);
            Assert.Contains("客户端不存在", result.Message);
        }

        [Fact]
        public async Task HertaLogin_AppId有效_正常签发()
        {
            var (service, clientApps, _) = Create();
            clientApps.Allowed.Add(AppId);

            var result = await service.HertaLoginAsync(LoginRequest());

            Assert.True(result.Code >= 200);
            Assert.Equal(AppId, clientApps.LastAppId);
            Assert.NotNull(result.Data);
            Assert.StartsWith("token-", result.Data!.Token);
        }
    }
}
