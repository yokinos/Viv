using System.Security.Cryptography;
using Viv.Delusion.Security;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
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

        private static (AccountService Service, StubApexAppService ClientApps, StubChatUserRepository Users) Create()
        {
            var users = new StubChatUserRepository();

            // 用框架的同一份实现生成口令：测试数据必须和生产写入的格式一致，
            // 否则「口令验证通过」这条路径永远测不到真实的哈希串格式。
            var (hash, salt) = PasswordHash.Hash("secret123");

            users.Users.Add(new EtChatUser
            {
                Id = 100,
                TenantId = SubjectId,
                UserType = EmUserType.EndUser,
                LoginCode = "alice",
                Password = hash,
                Salt = salt,
                NickName = "Alice"
            });

            var clientApps = new StubApexAppService();
            var service = new AccountService(users, new StubTokenService(), clientApps);
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

        [Fact]
        public async Task HertaLogin_低强度旧口令_登录后自动升级()
        {
            var (service, clientApps, users) = Create();
            clientApps.AllowAll = true;

            // 造一条「算法正确但迭代次数偏低」的口令串：将来把默认迭代调高后，存量口令就是这个形态
            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var weakKey = Rfc2898DeriveBytes.Pbkdf2(
                "secret123", saltBytes, 1_000, HashAlgorithmName.SHA256, 32);
            users.Users[0].Password = $"pbkdf2-sha256$1000${Convert.ToBase64String(weakKey)}";
            users.Users[0].Salt = Convert.ToBase64String(saltBytes);

            var result = await service.HertaLoginAsync(LoginRequest());

            Assert.True(result.Code >= 200);
            Assert.Equal(1, users.UpdateCalls);
            Assert.StartsWith("pbkdf2-sha256$100000$", users.Users[0].Password);
        }

        [Fact]
        public async Task HertaLogin_归属与类型取自库里那一行()
        {
            var (service, clientApps, users) = Create();
            clientApps.AllowAll = true;
            users.Users[0].UserType = EmUserType.CompanyUser;

            await service.HertaLoginAsync(LoginRequest());

            // 请求里没有 UserType 字段，能拿到 CompanyUser 只可能来自库
            Assert.Equal((int)EmUserType.CompanyUser, clientApps.LastUserType);
            Assert.Equal(SubjectId, clientApps.LastSubjectId);
        }

        [Fact]
        public async Task HertaLogin_主体未开通该应用_拒绝且不签发token()
        {
            var (service, clientApps, _) = Create();
            clientApps.Allowed.Add(AppId);
            clientApps.AllowedSubjectId = SubjectId + 1;

            var result = await service.HertaLoginAsync(LoginRequest());

            Assert.False(result.Code >= 200);
            Assert.Contains("未开通", result.Message);
        }

        [Fact]
        public async Task 注册_一律落C端用户()
        {
            var (service, clientApps, users) = Create();
            clientApps.AllowAll = true;

            var result = await service.RegisterAsync(new HertaRegisterRequest
            {
                SubjectId = SubjectId,
                AppId = AppId,
                LoginCode = "bob",
                Password = "secret123"
            });

            Assert.True(result.Code >= 200);
            Assert.Equal(EmUserType.EndUser, users.Users.Single(x => x.LoginCode == "bob").UserType);
        }

        [Fact]
        public async Task 注册_主体未开通该应用_拒绝且不落库()
        {
            var (service, clientApps, users) = Create();
            clientApps.Allowed.Add(AppId);
            clientApps.AllowedSubjectId = SubjectId + 1;

            var result = await service.RegisterAsync(new HertaRegisterRequest
            {
                SubjectId = SubjectId,
                AppId = AppId,
                LoginCode = "bob",
                Password = "secret123"
            });

            Assert.False(result.Code >= 200);
            Assert.Contains("未开通", result.Message);
            Assert.DoesNotContain(users.Users, x => x.LoginCode == "bob");
        }

        [Fact]
        public async Task 注册_按C端用户查租户授权表()
        {
            var (service, clientApps, _) = Create();
            clientApps.AllowAll = true;

            await service.RegisterAsync(new HertaRegisterRequest
            {
                SubjectId = SubjectId,
                AppId = AppId,
                LoginCode = "bob",
                Password = "secret123"
            });

            Assert.Equal((int)EmUserType.EndUser, clientApps.LastUserType);
            Assert.Equal(SubjectId, clientApps.LastSubjectId);
        }

        /// <summary>
        /// 必填项必须在基类校验这一层就被挡住：值类型 long 上标 [Required] 是无效的，
        /// 只有换成 long? 才会真正因为「没传」而失败。这条测试就是防止以后又改回 long。
        /// </summary>
        [Fact]
        public void 注册请求_缺必填项由基类校验拦截()
        {
            // Version 是基类的必填项（Range 1000-9999），先填上免得它先报错
            var missingSubject = new HertaRegisterRequest
            {
                Version = 1000, AppId = AppId, LoginCode = "bob", Password = "secret123"
            };
            Assert.Contains("租户", missingSubject.Validate());

            var missingApp = new HertaRegisterRequest
            {
                Version = 1000, SubjectId = SubjectId, LoginCode = "bob", Password = "secret123"
            };
            Assert.Contains("AppId", missingApp.Validate());

            // 传 0 与没传是两回事：[Required] 放行、[Range] 拦住
            var zeroApp = new HertaRegisterRequest
            {
                Version = 1000, SubjectId = SubjectId, AppId = 0, LoginCode = "bob", Password = "secret123"
            };
            Assert.Contains("AppId", zeroApp.Validate());

            var valid = new HertaRegisterRequest
            {
                Version = 1000, SubjectId = SubjectId, AppId = AppId, LoginCode = "bob", Password = "secret123"
            };
            Assert.Equal(string.Empty, valid.Validate());
        }

        [Fact]
        public void 登录请求_缺必填项由基类校验拦截()
        {
            var missingSubject = new HertaLoginRequest
            {
                Version = 1000, AppId = AppId, LoginCode = "alice", Password = "secret123"
            };
            Assert.Contains("租户", missingSubject.Validate());

            var missingApp = new HertaLoginRequest
            {
                Version = 1000, SubjectId = SubjectId, LoginCode = "alice", Password = "secret123"
            };
            Assert.Contains("AppId", missingApp.Validate());

            var valid = new HertaLoginRequest
            {
                Version = 1000, SubjectId = SubjectId, AppId = AppId, LoginCode = "alice", Password = "secret123"
            };
            Assert.Equal(string.Empty, valid.Validate());
        }
    }
}
