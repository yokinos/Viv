using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine;
using Viv.Entity.Database.Herta;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Entity.Vo.Account;
using Viv.Herta.Core.IRepository;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Core.Service
{
    public class AccountService : IAccountService
    {
        private readonly IChatUserRepository _users;
        private readonly IPasswordHasher _hasher;
        private readonly ITokenService _tokens;

        public AccountService(IChatUserRepository users, IPasswordHasher hasher, ITokenService tokens)
        {
            _users = users;
            _hasher = hasher;
            _tokens = tokens;
        }

        public async Task<VivApiResult> RegisterAsync(HertaRegisterRequest request)
        {
            if (request.SubjectId <= 0)
                return VivApiResult.Failed("缺少租户");

            var loginCode = request.LoginCode?.Trim();
            if (string.IsNullOrEmpty(loginCode))
                return VivApiResult.Failed("登录账号不能为空");

            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
                return VivApiResult.Failed("密码至少 6 位");

            var existing = await _users.GetByLoginCodeAsync(request.SubjectId, loginCode);
            if (existing != null)
                return VivApiResult.Failed("账号已存在");

            var (hash, salt) = _hasher.Hash(request.Password);

            var chatUserId = await _users.AddAsync(new EtChatUser
            {
                TenantId = request.SubjectId,
                LoginCode = loginCode,
                Password = hash,
                Salt = salt,
                NickName = string.IsNullOrWhiteSpace(request.NickName) ? loginCode : request.NickName.Trim()
            });

            return VivApiResult<HertaRegisterOutput>.Success(new HertaRegisterOutput { ChatUserId = chatUserId });
        }

        public async Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request)
        {
            var loginCode = request.LoginCode?.Trim();
            if (string.IsNullOrEmpty(loginCode))
                return VivApiResult<HertaLoginOutput>.Failed("登录账号不能为空");

            var user = await _users.GetByLoginCodeAsync(request.SubjectId, loginCode);

            // 账号不存在与密码错误返回同一句话，避免探出哪些账号存在
            if (user == null || !_hasher.Verify(request.Password, user.Password ?? string.Empty, user.Salt ?? string.Empty))
                return VivApiResult<HertaLoginOutput>.Failed("账号或密码错误");

            var options = _tokens.GetOptions();

            var token = _tokens.GenerateToken(new TokenPayload
            {
                AppId = request.AppId,
                SubjectId = user.TenantId,
                UserId = user.Id,
                UserName = user.NickName ?? loginCode
            });

            return VivApiResult<HertaLoginOutput>.Success(new HertaLoginOutput
            {
                Token = token,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(options.ExpireMinutes),
                ChatUserId = user.Id,
                NickName = user.NickName,
                Avatar = user.Avatar
            });
        }
    }
}
