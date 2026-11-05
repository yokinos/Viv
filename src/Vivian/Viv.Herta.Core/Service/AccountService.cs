using Viv.Apex.Proxy.Ports;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
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
        private readonly IProxyApexAppService _clientApps;

        public AccountService(IChatUserRepository users, IPasswordHasher hasher, ITokenService tokens, IProxyApexAppService clientApps)
        {
            _users = users;
            _hasher = hasher;
            _tokens = tokens;
            _clientApps = clientApps;
        }

        public async Task<VivApiResult> RegisterAsync(HertaRegisterRequest request)
        {
            var subjectId = request.SubjectId.GetValueOrDefault();
            var appId = request.AppId.GetValueOrDefault();

            var loginCode = request.LoginCode?.Trim();
            if (string.IsNullOrEmpty(loginCode))
                return VivApiResult.Failed("登录账号不能为空");

            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
                return VivApiResult.Failed("密码至少 6 位");

            var clientCheck = await _clientApps.CheckAsync(subjectId, appId, (int)EmUserType.EndUser);
            if (!clientCheck.Allowed)
                return VivApiResult.Failed(clientCheck.Reason);

            var existing = await _users.GetByLoginCodeAsync(subjectId, loginCode);
            if (existing != null)
                return VivApiResult.Failed("账号已存在");

            var (hash, salt) = _hasher.Hash(request.Password);

            var chatUserId = await _users.AddAsync(new EtChatUser
            {
                TenantId = subjectId,
                // 注册接口一律产出 C 端用户。UserType 决定「查哪张授权表」，一旦允许请求指定，
                // 填 Master 就等于跳过全部授权校验，所以这个字段只能由服务端写。
                UserType = EmUserType.EndUser,
                LoginCode = loginCode,
                Password = hash,
                Salt = salt,
                NickName = string.IsNullOrWhiteSpace(request.NickName) ? loginCode : request.NickName.Trim()
            });

            return VivApiResult<HertaRegisterOutput>.Success(new HertaRegisterOutput { ChatUserId = chatUserId });
        }

        public async Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request)
        {
            // 必填与范围由基类校验保证；0 会一路被下游按「缺少」拒绝，不需要在这里重复判
            var subjectId = request.SubjectId.GetValueOrDefault();
            var appId = request.AppId.GetValueOrDefault();

            var loginCode = request.LoginCode?.Trim();
            if (string.IsNullOrEmpty(loginCode))
                return VivApiResult<HertaLoginOutput>.Failed("登录账号不能为空");

            var user = await _users.GetByLoginCodeAsync(subjectId, loginCode);

            // 账号不存在与密码错误返回同一句话，避免探出哪些账号存在
            if (user == null || !_hasher.Verify(request.Password, user.Password ?? string.Empty, user.Salt ?? string.Empty))
                return VivApiResult<HertaLoginOutput>.Failed("账号或密码错误");

            // 归属与类型一律取库里那一行，不看请求：请求里的 SubjectId 只用于定位账号，
            // token 里的 SubjectId 也来自 user.TenantId。否则改个 Request 就能冒用别人的数据域。
            // 放在验密码之后：先验明身份，再谈「这个身份买了没买这个 App」，避免未授权探测。
            var clientCheck = await _clientApps.CheckAsync(user.TenantId, appId, (int)user.UserType);
            if (!clientCheck.Allowed)
                return VivApiResult<HertaLoginOutput>.Failed(clientCheck.Reason);

            var options = _tokens.GetOptions();

            var token = _tokens.GenerateToken(new TokenPayload
            {
                AppId = appId,
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
