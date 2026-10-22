using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.Entity.Vo.Chat;
using Viv.Herta.Core.IRepository;
using Viv.Herta.Core.IService;
using Viv.Herta.Core.Magic;
using Viv.Herta.Core.Mapping;
using Viv.Outbox;

namespace Viv.Herta.Core.Service
{
    public class ChatService : IChatService
    {
        /// <summary>EtGroupChatMember.MemberRole：0 普通成员 1 管理员 2 群主</summary>
        private const int GroupMemberRole = 0;
        private const int GroupAdminRole = 1;
        private const int GroupOwnerRole = 2;

        private const int MaxGroupMembers = 500;

        private static readonly TimeSpan RecallWindow = TimeSpan.FromMinutes(2);
        private readonly IVivOutbox _outbox;
        private readonly IVivContext _context;
        private readonly IChatSessionRepository _sessions;
        private readonly IChatMessageRepository _messages;
        private readonly IChatGroupRepository _groups;
        private readonly IChatGroupMemberRepository _groupMembers;
        private readonly IPresenceStore _presence;

        public ChatService(
            IVivOutbox outbox,
            IVivContext context,
            IChatSessionRepository sessions,
            IChatMessageRepository messages,
            IChatGroupRepository groups,
            IChatGroupMemberRepository groupMembers,
            IPresenceStore presence)
        {
            _outbox = outbox;
            _context = context;
            _sessions = sessions;
            _messages = messages;
            _groups = groups;
            _groupMembers = groupMembers;
            _presence = presence;
        }

        /// <summary>
        /// 消息落库与入队同事务：提交了才可能被投递，回滚了两边都看不到这条。
        /// </summary>
        [VivUnitOfWork]
        public virtual async Task<VivApiResult> SendMessageAsync(SendMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return VivApiResult.Failed("消息内容不能为空");

            // 消息体按客户端传来的 JSON 原样入库，解析只为校验
            var message = HertaMagic.GetChatMessage(request.MessageType, request.Message);
            if (message == null)
                return VivApiResult.Failed("消息错误");

            // 发送者：请求体给了 FromUserId 就是代发，没给用登录身份。
            // 租户不从请求里取，永远是登录身份的租户；跨租户代发只走事件（见 §26）
            var fromUserId = request.FromUserId > 0 ? request.FromUserId : _context.UserId;
            var tenantId = _context.SubjectId;
            if (fromUserId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            // 对端 Id 必须合法：TargetId = 0 会建出 TargetUserId = 0 的会话，
            // 并落一条 ReceiverUserId = 0 的消息，后续谁都查不到它
            if (request.TargetId <= 0)
                return VivApiResult.Failed("接收方不合法");

            var sentAt = DateTimeOffset.UtcNow;
            var isGroup = request.ReceiverType == EmChatReceiverType.Group;

            // 群消息必须校验成员：不然谁都能往任意群Id发消息，扇出给全体成员。
            // 排在建会话之前，非成员不产生任何写入
            if (isGroup && !await _groupMembers.IsMemberAsync(tenantId, request.TargetId, fromUserId))
                return VivApiResult.Failed("不在该群");

            // 单聊的两个会话行一次查回，省掉第二次「查不到再建」的往返
            EtChatSession senderSession;
            EtChatSession? receiverSession = null;

            if (isGroup)
            {
                senderSession = await _sessions.GetOrCreateAsync(tenantId, fromUserId, request.ReceiverType, request.TargetId);
            }
            else
            {
                var pair = await _sessions.FindDirectPairAsync(tenantId, fromUserId, request.TargetId);

                senderSession = pair.FirstOrDefault(x => x.UserId == fromUserId)
                    ?? await _sessions.GetOrCreateAsync(tenantId, fromUserId, EmChatReceiverType.User, request.TargetId);

                // 接收方也要有一行，否则它在自己的会话列表里看不到这条会话
                receiverSession = pair.FirstOrDefault(x => x.UserId == request.TargetId)
                    ?? await _sessions.GetOrCreateAsync(tenantId, request.TargetId, EmChatReceiverType.User, fromUserId);
            }

            var messageId = await _messages.AppendAsync(new ChatMessageEntry
            {
                TenantId = tenantId,
                SessionId = senderSession.Id,
                ReceiverType = request.ReceiverType,
                FromUserId = fromUserId,
                ToUserId = isGroup ? null : request.TargetId,
                GroupId = isGroup ? request.TargetId : null,
                MessageType = request.MessageType,
                Body = request.Message ?? string.Empty,
                SentAt = sentAt.UtcDateTime
            });

            // 两行的最后消息时间合成一条 UPDATE；逐行 Touch 每行都要先按主键查一次
            await _sessions.TouchManyAsync(
                receiverSession == null ? [senderSession.Id] : [senderSession.Id, receiverSession.Id],
                sentAt.UtcDateTime);

            if (isGroup)
            {
                // 群聊的最后消息时间是群级属性，所有成员共用一行
                await _groups.TouchLastMessageAsync(tenantId, request.TargetId, sentAt.UtcDateTime);
            }

            await _outbox.EnqueueAsync(new SendMessageEvent(
                fromUserId, request.TargetId, message, request.ReceiverType, request.MessageType)
            {
                MessageId = messageId,
                SentAt = sentAt,
                // 普通发送时事件体的租户与信封一致；模拟其他租户的生产者自己填
                TenantId = tenantId
            });

            return VivApiResult<SendMessageOutput>.Success(new SendMessageOutput
            {
                MessageId = messageId,
                SessionId = senderSession.Id,
                SentAt = sentAt.UtcDateTime
            });
        }

        /// <inheritdoc />
        public virtual async Task<VivApiResult> PullMessagesAsync(PullMessagesRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            if (request.TargetId <= 0)
                return VivApiResult.Failed("接收方不合法");

            // 群消息必须校验成员，否则知道 GroupId 就能拉全群历史。
            // 与发送路径同一道门，只是这里查的是读权限
            if (request.ReceiverType == EmChatReceiverType.Group
                && !await _groupMembers.IsMemberAsync(tenantId, request.TargetId, userId))
                return VivApiResult.Failed("不在该群");

            // 按对端取，不经 session：接收方可能还没有自己的会话行
            var entries = await _messages.ListAsync(
                tenantId, request.ReceiverType, userId, request.TargetId,
                request.AfterMessageId, request.BeforeMessageId, request.Limit);

            // 字段映射由 Mapperly 在编译期生成，SentAt/RecallAt 走 UtcTime 的 UTC 标注
            var items = entries.Select(ChatMappers.ToItem).ToList();

            return VivApiResult<List<MessageItemOutput>>.Success(items);
        }

        /// <inheritdoc />
        public virtual async Task<VivApiResult> ListSessionsAsync(int pageIndex, int pageSize)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var directs = await _sessions.GetDirectSessionsAsync(tenantId, userId);
            var memberships = await _groupMembers.GetMembershipsAsync(tenantId, userId);
            var groups = await _groups.GetListAsync(tenantId, memberships.Select(x => x.GroupChatId).Distinct().ToList());

            var items = new List<ChatSessionItemOutput>();

            foreach (var session in directs)
            {
                if (session.TargetUserId is not > 0) continue;

                items.Add(new ChatSessionItemOutput
                {
                    ReceiverType = EmChatReceiverType.User,
                    TargetId = session.TargetUserId.Value,
                    LastMessageAt = ToOffset(session.LastMessageTime),
                    IsTop = session.IsTop,
                    NoDisturb = session.NoDisturb,
                    LastReadMsgId = session.LastReadMsgId
                });
            }

            foreach (var group in groups)
            {
                var membership = memberships.FirstOrDefault(x => x.GroupChatId == group.Id);

                items.Add(new ChatSessionItemOutput
                {
                    ReceiverType = EmChatReceiverType.Group,
                    TargetId = group.Id,
                    Name = group.Name,
                    AvatarUrl = group.AvatarUrl,
                    LastMessageAt = ToOffset(group.LastMessageAt),
                    IsTop = membership?.IsTopSession ?? false,
                    NoDisturb = membership?.NoDisturb ?? false,
                    LastReadMsgId = membership?.LastReadMsgId ?? 0
                });
            }

            var take = Math.Clamp(pageSize, 1, 100);
            var skip = (Math.Max(1, pageIndex) - 1) * take;

            var page = items
                .OrderByDescending(x => x.IsTop)
                .ThenByDescending(x => x.LastMessageAt ?? DateTimeOffset.MinValue)
                .Skip(skip)
                .Take(take)
                .ToList();

            // 未读只算当前页，避免每个会话一次 count
            foreach (var item in page)
            {
                item.UnreadCount = await _messages.CountUnreadAsync(
                    tenantId, item.ReceiverType, userId, item.TargetId, item.LastReadMsgId);
            }

            return VivApiResult<List<ChatSessionItemOutput>>.Success(page);
        }

        /// <inheritdoc />
        public virtual async Task<VivApiResult> MarkReadAsync(MarkReadRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            if (request.ReceiverType == EmChatReceiverType.Group)
            {
                await _groupMembers.SetLastReadAsync(tenantId, request.TargetId, userId, request.LastReadMessageId);
            }
            else
            {
                await _sessions.SetLastReadAsync(
                    tenantId, userId, EmChatReceiverType.User, request.TargetId, request.LastReadMessageId);
            }

            return VivApiResult.Success();
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> CreateGroupAsync(CreateGroupRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return VivApiResult.Failed("群名称不能为空");

            // 群主由登录身份决定，请求里的成员列表只算其他成员
            var members = (request.MemberUserIds ?? [])
                .Where(x => x > 0 && x != userId)
                .Distinct()
                .ToList();

            if (members.Count + 1 > MaxGroupMembers)
                return VivApiResult.Failed($"群成员不能超过 {MaxGroupMembers} 人");

            var now = DateTime.UtcNow;
            var groupId = await _groups.AddAsync(new EtGroupChat
            {
                TenantId = tenantId,
                Name = name,
                AvatarUrl = request.AvatarUrl,
                OwnerUserId = userId,
                MaxMemberCount = MaxGroupMembers,
                CreateAt = now
            });

            var rows = new List<EtGroupChatMember>
            {
                new() { TenantId = tenantId, GroupChatId = groupId, UserId = userId, MemberRole = GroupOwnerRole, JoinAt = now }
            };
            rows.AddRange(members.Select(x => new EtGroupChatMember
            {
                TenantId = tenantId,
                GroupChatId = groupId,
                UserId = x,
                MemberRole = GroupMemberRole,
                JoinAt = now
            }));

            await _groupMembers.AddMembersAsync(rows);

            // 群主自己的会话行，让这个群出现在自己的会话列表里
            await _sessions.GetOrCreateAsync(tenantId, userId, EmChatReceiverType.Group, groupId);

            // 建群也要发成员变更：否则已在线的新成员不会进 SignalR 组，得等重连才收得到群消息。
            // 群主一起带上，多端场景下他的其他设备也要进组
            await _outbox.EnqueueAsync(new MembershipChangedEvent
            {
                TenantId = tenantId,
                GroupId = groupId,
                UserIds = rows.Select(x => x.UserId).ToList(),
                ChangeType = MembershipChangeType.Joined
            });

            return VivApiResult<CreateGroupOutput>.Success(new CreateGroupOutput { GroupId = groupId });
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> AddGroupMembersAsync(AddGroupMembersRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var group = await _groups.GetAsync(tenantId, request.GroupId);
            if (group == null)
                return VivApiResult.Failed("群不存在");

            var @operator = await _groupMembers.GetMemberAsync(tenantId, request.GroupId, userId);
            if (@operator == null)
                return VivApiResult.Failed("不在该群");

            // 群主与管理员可以直接拉人，普通成员取决于群的 AllowMemberInvite
            var canInvite = @operator.MemberRole is GroupOwnerRole or GroupAdminRole || group.AllowMemberInvite;
            if (!canInvite)
                return VivApiResult.Failed("没有邀请权限");

            var userIds = (request.UserIds ?? [])
                .Where(x => x > 0 && x != userId)
                .Distinct()
                .ToList();

            if (userIds.Count == 0)
                return VivApiResult.Failed("没有要加入的成员");

            var existing = await _groupMembers.GetGroupMembersAsync(tenantId, request.GroupId);
            var existingIds = existing.Select(x => x.UserId).ToHashSet();
            var toAdd = userIds.Where(x => !existingIds.Contains(x)).ToList();

            if (toAdd.Count == 0)
                return VivApiResult.Success();

            if (existing.Count + toAdd.Count > group.MaxMemberCount)
                return VivApiResult.Failed($"群成员不能超过 {group.MaxMemberCount} 人");

            var now = DateTime.UtcNow;
            await _groupMembers.AddMembersAsync(toAdd.Select(x => new EtGroupChatMember
            {
                TenantId = tenantId,
                GroupChatId = request.GroupId,
                UserId = x,
                MemberRole = GroupMemberRole,
                JoinAt = now
            }).ToList());

            await _outbox.EnqueueAsync(new MembershipChangedEvent
            {
                TenantId = tenantId,
                GroupId = request.GroupId,
                UserIds = toAdd,
                ChangeType = MembershipChangeType.Joined
            });

            return VivApiResult.Success();
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> RemoveGroupMembersAsync(RemoveGroupMembersRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var group = await _groups.GetAsync(tenantId, request.GroupId);
            if (group == null)
                return VivApiResult.Failed("群不存在");

            var @operator = await _groupMembers.GetMemberAsync(tenantId, request.GroupId, userId);
            if (@operator == null)
                return VivApiResult.Failed("不在该群");

            // 不传就是自己退群
            var targets = (request.UserIds ?? []).Where(x => x > 0).Distinct().ToList();
            if (targets.Count == 0) targets = [userId];

            var isManager = @operator.MemberRole is GroupOwnerRole or GroupAdminRole;
            if (targets.Any(x => x != userId) && !isManager)
                return VivApiResult.Failed("没有移除权限");

            // 群主退群会让群失去归属，先不允许
            if (targets.Contains(group.OwnerUserId))
                return VivApiResult.Failed("群主不能退群");

            var now = DateTime.UtcNow;
            var removed = new List<long>();
            foreach (var target in targets)
            {
                if (await _groupMembers.RemoveMemberAsync(tenantId, request.GroupId, target, now))
                    removed.Add(target);
            }

            if (removed.Count == 0)
                return VivApiResult.Success();

            await _outbox.EnqueueAsync(new MembershipChangedEvent
            {
                TenantId = tenantId,
                GroupId = request.GroupId,
                UserIds = removed,
                ChangeType = MembershipChangeType.Left
            });

            return VivApiResult.Success();
        }

        public virtual async Task<VivApiResult> ListGroupMembersAsync(long groupId)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var @operator = await _groupMembers.GetMemberAsync(tenantId, groupId, userId);
            if (@operator == null)
                return VivApiResult.Failed("不在该群");

            var members = await _groupMembers.GetGroupMembersAsync(tenantId, groupId);

            var items = members.Select(x => new GroupMemberItemOutput
            {
                UserId = x.UserId,
                MemberRole = x.MemberRole,
                GroupNickName = x.GroupNickName,
                JoinAt = new DateTimeOffset(DateTime.SpecifyKind(x.JoinAt, DateTimeKind.Utc))
            }).ToList();

            return VivApiResult<List<GroupMemberItemOutput>>.Success(items);
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> RecallMessageAsync(RecallMessageRequest request)
        {
            var userId = _context.UserId;
            var tenantId = _context.SubjectId;
            if (userId <= 0 || tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var entry = await _messages.GetAsync(tenantId, request.MessageId, request.ReceiverType);
            if (entry == null)
                return VivApiResult.Failed("消息不存在");

            if (entry.FromUserId != userId)
                return VivApiResult.Failed("只能撤回自己发的消息");

            // 撤回事件按对端扇出，对不上就不知道该推给谁
            var entryTarget = entry.ReceiverType == EmChatReceiverType.Group ? entry.GroupId : entry.ToUserId;
            if (entry.ReceiverType != request.ReceiverType || entryTarget != request.TargetId)
                return VivApiResult.Failed("消息不属于该会话");

            if (entry.IsRecall)
                return VivApiResult.Success();

            var now = DateTimeOffset.UtcNow;
            var sentAt = new DateTimeOffset(DateTime.SpecifyKind(entry.SentAt, DateTimeKind.Utc));
            if (now - sentAt > RecallWindow)
                return VivApiResult.Failed("超过撤回时限");

            await _messages.RecallAsync(tenantId, request.MessageId, request.ReceiverType, now.UtcDateTime);

            await _outbox.EnqueueAsync(new MessageRecalledEvent
            {
                TenantId = tenantId,
                ReceiverType = request.ReceiverType,
                TargetId = request.TargetId,
                MessageId = request.MessageId,
                RecalledAt = now
            });

            return VivApiResult.Success();
        }

        public virtual async Task<VivApiResult> GetPresenceAsync(IReadOnlyList<long> userIds)
        {
            var tenantId = _context.SubjectId;
            if (tenantId <= 0)
                return VivApiResult.Failed("缺少登录身份");

            var targets = userIds.Where(x => x > 0).Distinct().ToList();
            if (targets.Count == 0)
                return VivApiResult<List<PresenceItemOutput>>.Success(new List<PresenceItemOutput>());

            var states = await _presence.IsOnlineAsync(tenantId, targets);

            var items = targets
                .Select(x => new PresenceItemOutput { UserId = x, Online = states.TryGetValue(x, out var online) ? online : null })
                .ToList();

            return VivApiResult<List<PresenceItemOutput>>.Success(items);
        }

        private static DateTimeOffset? ToOffset(DateTime? value) => UtcTime.FromUtc(value);
    }
}
