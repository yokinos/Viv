using Viv.Apex.Proxy.Ports;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
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
            var ownerSubjectId = request.SubjectId.GetValueOrDefault();
            var appId = request.AppId.GetValueOrDefault();

            // 授权问的是「这个主体买了这个 App 没」，参数是归属主体 Id，不是域
            var clientCheck = await _clientApps.CheckAsync(ownerSubjectId, appId, (int)EmUserType.EndUser);
            if (!clientCheck.Allowed)
                return VivApiResult.Failed(clientCheck.Reason);

            // 域：同一归属主体复用已有域，没有才新开一个。
            // 域是隔离键，与主体 Id 分开存 —— 以后调整主体归属不影响历史会话的可见性。
            var realmId = await ResolveRealmAsync(ownerSubjectId);

            // 账号名按「主体 + 账号」唯一，不按域：一个域里可以有多个主体的账号（跨主体对话），
            // 各自都叫 alice 是合法的。
            var existing = await _users.GetByLoginCodeAsync(ownerSubjectId, request.LoginCode);
            if (existing != null)
                return VivApiResult.Failed("账号已存在");

            var (hash, salt) = PasswordHash.Hash(request.Password);

            var chatUserId = await _users.AddAsync(new EtChatUser
            {
                RealmId = realmId,
                OwnerSubjectId = ownerSubjectId,
                UserType = EmUserType.EndUser,
                LoginCode = request.LoginCode,
                Password = hash,
                Salt = salt,
                NickName = string.IsNullOrWhiteSpace(request.NickName) ? request.LoginCode : request.NickName.Trim()
            });

            return VivApiResult<HertaRegisterOutput>.Success(new HertaRegisterOutput { ChatUserId = chatUserId });
        }

        /// <summary>
        /// 归属主体 → 聊天域。该主体已有账号就复用它所在的域，没有才新开一个。
        /// <para>
        /// 不直接把主体 Id 当域：域是 IM 的内部概念，两个不同主体可以被放进同一个域来表示
        /// 「它们互相看得见」，主体 Id 做不到这一点 —— 那正是跨主体对话要的能力。
        /// </para>
        /// </summary>
        private async Task<long> ResolveRealmAsync(long ownerSubjectId)
        {
            var peer = await _users.GetByOwnerAsync(ownerSubjectId).ConfigureAwait(false);
            return peer?.RealmId ?? IdMagic.NextId();
        }

        public async Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request)
        {
            // 必填与范围由基类校验保证；0 会一路被下游按「缺少」拒绝，不需要在这里重复判
            var ownerSubjectId = request.SubjectId.GetValueOrDefault();
            var appId = request.AppId.GetValueOrDefault();

            var loginCode = request.LoginCode?.Trim();
            if (string.IsNullOrEmpty(loginCode))
                return VivApiResult<HertaLoginOutput>.Failed("登录账号不能为空");

            var user = await _users.GetByLoginCodeAsync(ownerSubjectId, loginCode);

            // 账号不存在与密码错误返回同一句话，避免探出哪些账号存在
            if (user == null || !PasswordHash.Verify(user.Password, user.Salt, request.Password, out var needsRehash))
                return VivApiResult<HertaLoginOutput>.Failed("账号或密码错误");

            // 迭代次数写在哈希串里，调高默认值时老口令会在登录后自动升级；升级失败不影响本次登录
            if (needsRehash)
                await UpgradePasswordHashAsync(user, request.Password);

            // 归属与类型一律取库里那一行，不看请求：请求里的 SubjectId 只用于定位账号，
            // 域取 user.RealmId，主体取 user.OwnerSubjectId。
            // 放在验密码之后：先验明身份，再谈「这个身份买了没买这个 App」，避免未授权探测。
            var clientCheck = user.OwnerSubjectId is { } owner
                ? await _clientApps.CheckAsync(owner, appId, (int)user.UserType)
                // 无归属主体（纯 C 端账号）：-1 表示只确认应用本身可用，不查任何主体授权表
                : await _clientApps.CheckAsync(0, appId, -1);
            if (!clientCheck.Allowed)
                return VivApiResult<HertaLoginOutput>.Failed(clientCheck.Reason);

            var options = _tokens.GetOptions();

            var token = _tokens.GenerateToken(new TokenPayload
            {
                AppId = appId,
                // 聊天链路的 SubjectId 承载 RealmId：下游 ChatService / Kinna 准入把它当域用。
                // 主体 Id 只留在库里，不进 token —— 一旦进了，下游就会拿它当隔离键。
                SubjectId = user.RealmId,
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
