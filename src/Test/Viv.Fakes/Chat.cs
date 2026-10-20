using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.IRepository;
using Viv.Herta.Core.IService;

namespace Viv.Fakes;

/// <summary>IM 自有账号仓储替身。</summary>
public sealed class StubChatUserRepository : IChatUserRepository
{
    public List<EtChatUser> Users { get; set; } = new();

    public long NextChatUserId { get; set; } = 700;

    public Task<EtChatUser?> GetByLoginCodeAsync(long tenantId, string loginCode)
        => Task.FromResult(Users.FirstOrDefault(x => x.TenantId == tenantId && x.LoginCode == loginCode));

    public Task<long> AddAsync(EtChatUser user)
    {
        var id = NextChatUserId++;
        user.Id = id;
        Users.Add(user);
        return Task.FromResult(id);
    }
}

/// <summary>令牌服务替身：记录最后一份载荷，token 用 userId 拼接便于断言。</summary>
public sealed class StubTokenService : ITokenService
{
    public TokenOptions Options { get; set; } = new() { ExpireMinutes = 60 };

    public TokenPayload? LastPayload { get; private set; }

    public TokenOptions GetOptions() => Options;

    public string GenerateToken(TokenPayload payload)
    {
        LastPayload = payload;
        return $"token-{payload.UserId}";
    }

    public bool ValidateToken(string token) => true;

    public TokenPayload? ParseToken(string token) => null;
}

/// <summary>会话仓储替身：默认返回一行会话，可关掉模拟「还没有会话」。</summary>
public sealed class StubChatSessionRepository : IChatSessionRepository
{
    public EtChatSession Session { get; set; } = new() { Id = 100 };
    /// <summary>false = 查不到会话</summary>
    public bool SessionExists { get; set; } = true;

    /// <summary>会话列表里该用户自己的单聊行</summary>
    public List<EtChatSession> DirectSessions { get; set; } = new();

    public List<(long SessionId, DateTime At)> Touched { get; } = new();

    public long? LastTargetId { get; private set; }

    public EmChatReceiverType? LastReceiverType { get; private set; }

    public Task<EtChatSession?> FindAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId)
    {
        Record(receiverType, targetId);
        return Task.FromResult(SessionExists ? Session : null);
    }

    public Task<EtChatSession> GetOrCreateAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId)
    {
        Record(receiverType, targetId);
        return Task.FromResult(Session);
    }

    public Task<List<EtChatSession>> GetDirectSessionsAsync(long tenantId, long userId)
        => Task.FromResult(DirectSessions);

    public List<(long TargetId, long LastReadMsgId)> LastReadWrites { get; } = new();

    public Task SetLastReadAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId, long lastReadMessageId)
    {
        LastReadWrites.Add((targetId, lastReadMessageId));
        return Task.CompletedTask;
    }

    public Task TouchAsync(long sessionId, DateTime lastMessageTime)
    {
        Touched.Add((sessionId, lastMessageTime));
        return Task.CompletedTask;
    }

    private void Record(EmChatReceiverType receiverType, long targetId)
    {
        LastReceiverType = receiverType;
        LastTargetId = targetId;
    }
}

/// <summary>消息仓储替身：记录写入的消息，拉取结果可预设。</summary>
public sealed class StubChatMessageRepository : IChatMessageRepository
{
    public List<ChatMessageEntry> Appended { get; } = new();

    public List<ChatMessageEntry> PullResult { get; set; } = new();

    /// <summary>下一条写入分配的行Id</summary>
    public long NextId { get; set; } = 500;

    public long? LastAfterMessageId { get; private set; }

    public long? LastBeforeMessageId { get; private set; }

    public long? LastTargetId { get; private set; }

    public long? LastUserId { get; private set; }

    public Task<long> AppendAsync(ChatMessageEntry entry)
    {
        var id = NextId++;
        Appended.Add(entry with { Id = id });
        return Task.FromResult(id);
    }

    public Task<List<ChatMessageEntry>> ListAsync(
        long tenantId, EmChatReceiverType receiverType, long userId, long targetId,
        long afterMessageId, long beforeMessageId, int limit)
    {
        LastAfterMessageId = afterMessageId;
        LastBeforeMessageId = beforeMessageId;
        LastUserId = userId;
        LastTargetId = targetId;
        return Task.FromResult(PullResult.Take(limit).ToList());
    }

    /// <summary>GetAsync 的返回值</summary>
    public ChatMessageEntry? Entry { get; set; }

    public List<(long MessageId, DateTime RecalledAt)> Recalled { get; } = new();

    public Task<ChatMessageEntry?> GetAsync(long tenantId, long messageId, EmChatReceiverType receiverType)
        => Task.FromResult(Entry);

    public Task<bool> RecallAsync(long tenantId, long messageId, EmChatReceiverType receiverType, DateTime recalledAt)
    {
        Recalled.Add((messageId, recalledAt));
        return Task.FromResult(true);
    }

    /// <summary>预设每个对端的未读条数，未列出的按 0</summary>
    public Dictionary<long, int> UnreadByTarget { get; set; } = new();

    public Task<int> CountUnreadAsync(
        long tenantId, EmChatReceiverType receiverType, long userId, long targetId, long lastReadMessageId)
        => Task.FromResult(UnreadByTarget.TryGetValue(targetId, out var count) ? count : 0);
}

/// <summary>群成员仓储替身：成员判定与成员行可预设。</summary>
public sealed class StubChatGroupMemberRepository : IChatGroupMemberRepository
{
    public bool IsMember { get; set; } = true;

    public List<EtGroupChatMember> Memberships { get; set; } = new();

    public Task<List<long>> GetGroupIdsAsync(long tenantId, long userId)
        => Task.FromResult(Memberships.Select(x => x.GroupChatId).ToList());

    public List<EtGroupChatMember> AddedMembers { get; } = new();

    public Task AddMembersAsync(List<EtGroupChatMember> members)
    {
        AddedMembers.AddRange(members);
        return Task.CompletedTask;
    }

    public Task<List<EtGroupChatMember>> GetMembershipsAsync(long tenantId, long userId)
        => Task.FromResult(Memberships);

    public List<(long GroupId, long LastReadMsgId)> LastReadWrites { get; } = new();

    public Task SetLastReadAsync(long tenantId, long groupId, long userId, long lastReadMessageId)
    {
        LastReadWrites.Add((groupId, lastReadMessageId));
        return Task.CompletedTask;
    }

    public Task<bool> IsMemberAsync(long tenantId, long groupId, long userId) => Task.FromResult(IsMember);

    /// <summary>GetMemberAsync 的返回值，用来摆操作者身份</summary>
    public EtGroupChatMember? Operator { get; set; }

    public List<EtGroupChatMember> GroupMembers { get; set; } = new();

    public List<(long GroupId, long UserId)> Removed { get; } = new();

    public Task<EtGroupChatMember?> GetMemberAsync(long tenantId, long groupId, long userId)
        => Task.FromResult(Operator);

    public Task<List<EtGroupChatMember>> GetGroupMembersAsync(long tenantId, long groupId)
        => Task.FromResult(GroupMembers);

    public Task<bool> RemoveMemberAsync(long tenantId, long groupId, long userId, DateTime quitAt)
    {
        Removed.Add((groupId, userId));
        return Task.FromResult(true);
    }
}

/// <summary>在线态替身：状态可预设，写入动作记录在案。</summary>
public sealed class StubPresenceStore : IPresenceStore
{
    public Dictionary<long, bool?> States { get; set; } = new();

    public List<(long TenantId, long UserId)> OnlineUsers { get; } = new();

    public List<(long TenantId, long UserId)> OfflineUsers { get; } = new();

    public List<(long TenantId, long UserId)> RenewedUsers { get; } = new();

    public Task OnlineAsync(long tenantId, long userId)
    {
        OnlineUsers.Add((tenantId, userId));
        return Task.CompletedTask;
    }

    public Task OfflineAsync(long tenantId, long userId)
    {
        OfflineUsers.Add((tenantId, userId));
        return Task.CompletedTask;
    }

    public Task RenewAsync(long tenantId, long userId)
    {
        RenewedUsers.Add((tenantId, userId));
        return Task.CompletedTask;
    }

    public Task<bool?> IsOnlineAsync(long tenantId, long userId)
        => Task.FromResult(States.TryGetValue(userId, out var state) ? state : null);

    public Task<Dictionary<long, bool?>> IsOnlineAsync(long tenantId, IReadOnlyList<long> userIds)
    {
        var result = new Dictionary<long, bool?>(userIds.Count);
        foreach (var userId in userIds)
            result[userId] = States.TryGetValue(userId, out var state) ? state : null;

        return Task.FromResult(result);
    }
}

/// <summary>群仓储替身：群列表可预设，记录最后消息时间的写入。</summary>
public sealed class StubChatGroupRepository : IChatGroupRepository
{
    public List<EtGroupChat> Groups { get; set; } = new();

    public List<(long GroupId, DateTime At)> Touched { get; } = new();

    public long NextGroupId { get; set; } = 900;

    public List<EtGroupChat> Created { get; } = new();

    public Task<long> AddAsync(EtGroupChat group)
    {
        var id = NextGroupId++;
        group.Id = id;
        Created.Add(group);
        return Task.FromResult(id);
    }

    public Task TouchLastMessageAsync(long tenantId, long groupId, DateTime lastMessageAt)
    {
        Touched.Add((groupId, lastMessageAt));
        return Task.CompletedTask;
    }

    /// <summary>GetAsync 的返回值</summary>
    public EtGroupChat? Group { get; set; }

    public Task<EtGroupChat?> GetAsync(long tenantId, long groupId) => Task.FromResult(Group);

    public Task<List<EtGroupChat>> GetListAsync(long tenantId, List<long> groupIds)
        => Task.FromResult(Groups.Where(x => groupIds.Contains(x.Id)).ToList());
}
