using Viv.Apex.Proxy.Ports;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Security;
using Viv.Engine;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Entity.Vo.Account;
using Viv.Herta.Core.IRepository;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Core.Service
{
    /// <summary>
    /// IM 账号注册与登录。
    /// 口令哈希不在这里实现，统一走框架的 <see cref="PasswordHash"/>（PBKDF2-SHA256 + 独立盐 + 固定时间比较）——
    /// 与 Apex 同源，避免同一套密码学逻辑维护两份。
    /// </summary>
    public class AccountService : IAccountService
    {
        private readonly IChatUserRepository _users;
        private readonly ITokenService _tokens;
        private readonly IProxyApexAppService _clientApps;

        public AccountService(IChatUserRepository users, ITokenService tokens, IProxyApexAppService clientApps)
        {
            _users = users;
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

            var (hash, salt) = PasswordHash.Hash(request.Password);

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
            if (user == null || !PasswordHash.Verify(user.Password, user.Salt, request.Password, out var needsRehash))
                return VivApiResult<HertaLoginOutput>.Failed("账号或密码错误");

            // 迭代次数写在哈希串里，调高默认值时老口令会在登录后自动升级；升级失败不影响本次登录
            if (needsRehash)
                await UpgradePasswordHashAsync(user, request.Password);

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

        /// <summary>
        /// 旧格式（或迭代次数偏低）口令登录成功后升级为当前强度，与 Apex 的 LoginImplBase 同口径。
        /// 写库失败就保持旧哈希：密码已经核对通过，不能因为升级失败把用户挡在门外，下次登录再试。
        /// </summary>
        private async Task UpgradePasswordHashAsync(EtChatUser user, string password)
        {
            try
            {
                var (hash, salt) = PasswordHash.Hash(password);
                user.Password = hash;
                user.Salt = salt;
                await _users.UpdateAsync(user).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 刻意吞掉：升级是尽力而为，失败不能影响本次登录
            }
        }
    }
}
