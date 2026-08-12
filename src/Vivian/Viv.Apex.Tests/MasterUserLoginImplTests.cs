using System.Threading.Tasks;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.Impl.Login;
using Viv.Delusion.Magic;
using Viv.Entity.Any;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    public class MasterUserLoginImplTests
    {
        private const long AppId = 7;
        private const long UserId = 42;
        private const string Phone = "13800138000";
        private const string Password = "pwd123";
        private const string Salt = "abc123";

        private static ApexLoginRequest CreateLoginRequest() => new()
        {
            AppId = AppId,
            Version = 1000,
            UserName = Phone,
            Password = Password,
            UserType = EmUserType.Master
        };

        private static ApexRefreshRequest CreateRefreshRequest(string refreshToken) => new()
        {
            AppId = AppId,
            Version = 1000,
            UserId = UserId,
            RefreshToken = refreshToken
        };

        private static AtUser CreateUser(EmStatus status = EmStatus.Normal) => new()
        {
            Id = UserId,
            UserType = EmUserType.Master,
            Name = "张三",
            NickName = "nick",
            AvatarUrl = "http://avatar/1.png",
            Phone = Phone,
            Salt = Salt,
            Password = EncryptMagic.HashMd5($"{Password}{Salt}"),
            Status = status
        };

        [Fact]
        public async Task LoginAsync_UserTypeNotMaster_ReturnsIllegalType()
        {
            var impl = new MasterUserLoginImpl(new StubUserRepository(), new StubTokenService(), new StubRedisService());
            var request = CreateLoginRequest();
            request.UserType = EmUserType.CompanyUser;

            var result = await impl.LoginAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("登录类型非法", result.Message);
        }

        [Fact]
        public async Task LoginAsync_UserNotFound_ReturnsAccountOrPasswordError()
        {
            var repo = new StubUserRepository { UserByPhone = null };
            var impl = new MasterUserLoginImpl(repo, new StubTokenService(), new StubRedisService());

            var result = await impl.LoginAsync(CreateLoginRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("账号或者密码错误", result.Message);
        }

        [Fact]
        public async Task LoginAsync_WrongPassword_ReturnsAccountOrPasswordError()
        {
            var user = CreateUser();
            user.Password = EncryptMagic.HashMd5($"wrongpass{Salt}");
            var repo = new StubUserRepository { UserByPhone = user };
            var impl = new MasterUserLoginImpl(repo, new StubTokenService(), new StubRedisService());

            var result = await impl.LoginAsync(CreateLoginRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("账号或者密码错误", result.Message);
        }

        [Fact]
        public async Task LoginAsync_DisabledAccount_ReturnsDisabled()
        {
            var repo = new StubUserRepository { UserByPhone = CreateUser(EmStatus.Disabled) };
            var impl = new MasterUserLoginImpl(repo, new StubTokenService(), new StubRedisService());

            var result = await impl.LoginAsync(CreateLoginRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("账号被禁用", result.Message);
        }

        [Fact]
        public async Task LoginAsync_Success_ReturnsOutputAndPersistsSession()
        {
            var repo = new StubUserRepository { UserByPhone = CreateUser() };
            var token = new StubTokenService();
            var redis = new StubRedisService();
            var impl = new MasterUserLoginImpl(repo, token, redis);

            var result = await impl.LoginAsync(CreateLoginRequest());

            Assert.True(result.IsSuccess);
            var output = result.Data;
            Assert.NotNull(output);
            Assert.Equal(UserId, output!.UserId);
            Assert.Equal("张三", output.Name);
            Assert.Equal("nick", output.NickName);
            Assert.Equal(Phone, output.Phone);
            Assert.Equal("http://avatar/1.png", output.AvatarUrl);
            Assert.Equal("access-token", output.AccessToken);
            Assert.Equal(64, output.RefreshToken!.Length);

            // Token 载荷
            Assert.Equal(1, token.GenerateCount);
            Assert.Equal(AppId, token.LastPayload!.AppId);
            Assert.Equal(UserId, token.LastPayload.UserId);
            Assert.Equal("张三", token.LastPayload.UserName);

            // Redis 会话持久化
            var (key, value) = Assert.Single(redis.Added);
            Assert.Equal("rt:apex:7:42", key);
            var session = Assert.IsType<RefreshTokenValue>(value);
            Assert.Equal(AppId, session.AppId);
            Assert.Equal(UserId, session.UserId);
            Assert.Equal(output.RefreshToken, session.RefreshToken);

            // 过期时间在 30~45 天内
            Assert.True(redis.LastExpire!.Value.TotalDays is >= 30 and <= 45);
        }

        [Fact]
        public async Task RefreshTokenAsync_SessionMissing_ReturnsExpired()
        {
            var redis = new StubRedisService { Session = null };
            var impl = new MasterUserLoginImpl(new StubUserRepository(), new StubTokenService(), redis);

            var result = await impl.RefreshTokenAsync(CreateRefreshRequest("old-refresh"));

            Assert.False(result.IsSuccess);
            Assert.Equal("登录凭证已失效，请重新登录", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_TokenMismatch_ReturnsExpired()
        {
            var redis = new StubRedisService
            {
                Session = new RefreshTokenValue { AppId = AppId, UserId = UserId, RefreshToken = "expected" }
            };
            var impl = new MasterUserLoginImpl(new StubUserRepository(), new StubTokenService(), redis);

            var result = await impl.RefreshTokenAsync(CreateRefreshRequest("different"));

            Assert.False(result.IsSuccess);
            Assert.Equal("登录凭证已失效，请重新登录", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_UserNotFound_ReturnsAccountError()
        {
            var redis = new StubRedisService
            {
                Session = new RefreshTokenValue { AppId = AppId, UserId = UserId, RefreshToken = "expected" }
            };
            var repo = new StubUserRepository { UserById = null };
            var impl = new MasterUserLoginImpl(repo, new StubTokenService(), redis);

            var result = await impl.RefreshTokenAsync(CreateRefreshRequest("expected"));

            Assert.False(result.IsSuccess);
            Assert.Equal("账号异常，请重新登录", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_UserDisabled_ReturnsAccountError()
        {
            var redis = new StubRedisService
            {
                Session = new RefreshTokenValue { AppId = AppId, UserId = UserId, RefreshToken = "expected" }
            };
            var repo = new StubUserRepository { UserById = CreateUser(EmStatus.Disabled) };
            var impl = new MasterUserLoginImpl(repo, new StubTokenService(), redis);

            var result = await impl.RefreshTokenAsync(CreateRefreshRequest("expected"));

            Assert.False(result.IsSuccess);
            Assert.Equal("账号异常，请重新登录", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_Success_ReturnsNewOutput()
        {
            var redis = new StubRedisService
            {
                Session = new RefreshTokenValue { AppId = AppId, UserId = UserId, RefreshToken = "old-refresh" }
            };
            var repo = new StubUserRepository { UserById = CreateUser() };
            var token = new StubTokenService();
            var impl = new MasterUserLoginImpl(repo, token, redis);

            var result = await impl.RefreshTokenAsync(CreateRefreshRequest("old-refresh"));

            Assert.True(result.IsSuccess);
            Assert.Equal(1, token.GenerateCount);

            var (key, value) = Assert.Single(redis.Added);
            Assert.Equal("rt:apex:7:42", key);
            var newSession = Assert.IsType<RefreshTokenValue>(value);
            Assert.Equal(AppId, newSession.AppId);
            Assert.Equal(UserId, newSession.UserId);
            Assert.NotEqual("old-refresh", newSession.RefreshToken);
            Assert.Equal(64, newSession.RefreshToken.Length);
            Assert.Equal(newSession.RefreshToken, result.Data!.RefreshToken);
        }

        [Fact]
        public async Task LogoutAsync_ReturnsFalse()
        {
            var impl = new MasterUserLoginImpl(new StubUserRepository(), new StubTokenService(), new StubRedisService());

            var result = await impl.LogoutAsync(new ApexLoginoutRequest());

            Assert.False(result);
        }
    }
}
