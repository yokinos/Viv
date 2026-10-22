using Viv.Contracts.Models;
using Viv.Delusion.Extension;
using Viv.Engine;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.Entity.Vo.Chat;
using Viv.Herta.Core.Service;

namespace Viv.Herta.Tests
{
    public class ChatServiceTests
    {
        private const long SubjectId = 1;

        /// <summary>登录用户，也是事件与落库记录里唯一合法的发送者</summary>
        private const long ContextUserId = 2;

        private const long TargetId = 5;

        private sealed record Sut(
            ChatService Service,
            RecordingOutbox Outbox,
            StubChatSessionRepository Sessions,
            StubChatMessageRepository Messages,
            StubChatGroupRepository Groups,
            StubChatGroupMemberRepository GroupMembers,
            StubPresenceStore Presence);

        private static Sut Create(long userId = ContextUserId)
        {
            var outbox = new RecordingOutbox();
            var sessions = new StubChatSessionRepository();
            var messages = new StubChatMessageRepository();
            var groups = new StubChatGroupRepository();
            var groupMembers = new StubChatGroupMemberRepository();
            var presence = new StubPresenceStore();
            var context = new TestContext
            {
                Snapshot = new VivContextContent { AppId = 7, SubjectId = SubjectId, UserId = userId }
            };

            return new Sut(
                new ChatService(outbox, context, sessions, messages, groups, groupMembers, presence),
                outbox,
                sessions,
                messages,
                groups,
                groupMembers,
                presence);
        }

        private static SendMessageRequest CreateRequest(EmChatReceiverType receiverType = EmChatReceiverType.User) => new()
        {
            // 不填发送者 = 用登录身份；代发的用例自己改这个字段
            FromUserId = 0,
            TargetId = TargetId,
            ReceiverType = receiverType,
            MessageType = EmChatMessageType.Text,
            Message = new TextMessage { Text = "hi" }.ToJson()
        };

        [Fact]
        public async Task SendMessage_落库并入队_两边用同一个消息Id()
        {
            var sut = Create();

            var result = await sut.Service.SendMessageAsync(CreateRequest());

            Assert.True(result.Code >= 200);

            var saved = Assert.Single(sut.Messages.Appended);
            Assert.Equal(sut.Sessions.Session.Id, saved.SessionId);
            Assert.Equal(ContextUserId, saved.FromUserId);
            Assert.Equal(TargetId, saved.ToUserId);
            Assert.Null(saved.GroupId);

            var evt = Assert.IsType<SendMessageEvent>(Assert.Single(sut.Outbox.Enqueued));
            Assert.Equal(saved.Id, evt.MessageId);
        }

        [Fact]
        public async Task SendMessage_单聊_两侧的会话行一次更新()
        {
            // 接收方没有自己的会话行时，它在会话列表里根本看不到这条会话；
            // 两行合成一条 UPDATE，不再逐行「先查再改」
            var sut = Create();

            await sut.Service.SendMessageAsync(CreateRequest());

            var call = Assert.Single(sut.Sessions.TouchedMany);
            Assert.Equal(2, call.SessionIds.Count);
            Assert.Empty(sut.Sessions.Touched);
        }

        [Fact]
        public async Task SendMessage_群聊_只更新群和发送方自己的会话()
        {
            var sut = Create();

            await sut.Service.SendMessageAsync(CreateRequest(EmChatReceiverType.Group));

            // 群聊的最后消息时间是群级属性，不给每个成员各写一行
            var touched = Assert.Single(sut.Groups.Touched);
            Assert.Equal(TargetId, touched.GroupId);
            Assert.Single(Assert.Single(sut.Sessions.TouchedMany).SessionIds);
        }

        [Fact]
        public async Task SendMessage_群聊_GroupId落库且ToUserId为空()
        {
            var sut = Create();

            await sut.Service.SendMessageAsync(CreateRequest(EmChatReceiverType.Group));

            var saved = Assert.Single(sut.Messages.Appended);
            Assert.Equal(TargetId, saved.GroupId);
            Assert.Null(saved.ToUserId);
        }

        [Fact]
        public async Task SendMessage_非群成员_拒绝且不落库()
        {
            // 不校验成员的话，谁都能往任意群Id发消息并扇出给全体成员
            var sut = Create();
            sut.GroupMembers.IsMember = false;

            var result = await sut.Service.SendMessageAsync(CreateRequest(EmChatReceiverType.Group));

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Appended);
            Assert.Empty(sut.Outbox.Enqueued);
            // 校验排在建会话之前 —— 非成员不该留下会话行
            Assert.Null(sut.Sessions.LastTargetId);
        }

        [Fact]
        public async Task SendMessage_对端为0_拒绝且不落库()
        {
            // 不拦会建出 TargetUserId = 0 的会话，并落一条谁都查不到的消息
            var sut = Create();
            var request = CreateRequest();
            request.TargetId = 0;

            var result = await sut.Service.SendMessageAsync(request);

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Appended);
            Assert.Empty(sut.Outbox.Enqueued);
        }

        [Fact]
        public async Task PullMessages_非群成员_拒绝()
        {
            // 知道 GroupId 就能拉全群历史，和发送路径是同一种越权
            var sut = Create();
            sut.GroupMembers.IsMember = false;

            var result = await sut.Service.PullMessagesAsync(new PullMessagesRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.Group
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Appended);
        }

        [Fact]
        public async Task SendMessage_不传发送者时用登录身份()
        {
            var sut = Create();

            await sut.Service.SendMessageAsync(CreateRequest());

            Assert.Equal(ContextUserId, Assert.Single(sut.Messages.Appended).FromUserId);
        }

        [Fact]
        public async Task SendMessage_传了发送者就代发()
        {
            // 机器人、客服代发：落库和事件都按请求体里的署名走
            var sut = Create();
            var request = CreateRequest();
            request.FromUserId = 999;

            await sut.Service.SendMessageAsync(request);

            Assert.Equal(999, Assert.Single(sut.Messages.Appended).FromUserId);

            var evt = Assert.IsType<SendMessageEvent>(Assert.Single(sut.Outbox.Enqueued));
            Assert.Equal(999, evt.FromUserId);
            Assert.Equal(SubjectId, evt.TenantId);
        }

        [Fact]
        public async Task SendMessage_指定其他租户的请求字段不存在_租户永远来自登录身份()
        {
            // 跨租户代发只能走事件，HTTP 侧没有 TenantId 这个概念
            var sut = Create();
            var request = CreateRequest();
            request.FromUserId = 999;

            await sut.Service.SendMessageAsync(request);

            Assert.Equal(SubjectId, Assert.Single(sut.Messages.Appended).TenantId);
            Assert.Equal(SubjectId, Assert.IsType<SendMessageEvent>(Assert.Single(sut.Outbox.Enqueued)).TenantId);
        }

        [Fact]
        public async Task SendMessage_没有登录身份_失败且不落库不入队()
        {
            var sut = Create(userId: 0);

            var result = await sut.Service.SendMessageAsync(CreateRequest());

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Appended);
            Assert.Empty(sut.Outbox.Enqueued);
        }

        [Fact]
        public async Task SendMessage_不支持的消息类型_失败且不落库()
        {
            var sut = Create();
            var request = CreateRequest();
            request.MessageType = unchecked((EmChatMessageType)999);
            request.Message = "{}";

            var result = await sut.Service.SendMessageAsync(request);

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Appended);
            Assert.Empty(sut.Outbox.Enqueued);
        }

        [Fact]
        public async Task PullMessages_按对端拉取_不依赖自己的会话行()
        {
            var sut = Create();
            sut.Messages.PullResult =
            [
                new ChatMessageEntry
                {
                    Id = 21,
                    SessionId = 100,
                    ReceiverType = EmChatReceiverType.User,
                    FromUserId = TargetId,
                    ToUserId = ContextUserId,
                    MessageType = EmChatMessageType.Text,
                    Body = "{\"text\":\"yo\"}",
                    SentAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
                }
            ];

            var result = await sut.Service.PullMessagesAsync(new PullMessagesRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.User,
                AfterMessageId = 20,
                Limit = 10
            });

            Assert.Equal(ContextUserId, sut.Messages.LastUserId);
            Assert.Equal(TargetId, sut.Messages.LastTargetId);
            Assert.Equal(20, sut.Messages.LastAfterMessageId);
            Assert.Equal(0, sut.Messages.LastBeforeMessageId);

            var typed = Assert.IsType<VivApiResult<List<MessageItemOutput>>>(result);
            var item = Assert.Single(typed.Data!);
            Assert.Equal(21, item.Id);
            Assert.Equal(EmChatMessageType.Text, item.MessageType);
            Assert.Equal(TimeSpan.Zero, item.SentAt.Offset);
        }

        [Fact]
        public async Task PullMessages_可以向前翻历史()
        {
            var sut = Create();

            await sut.Service.PullMessagesAsync(new PullMessagesRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.User,
                BeforeMessageId = 100,
                Limit = 20
            });

            Assert.Equal(100, sut.Messages.LastBeforeMessageId);
        }

        [Fact]
        public void PullMessagesRequest_两个游标不能同时给()
        {
            var request = new PullMessagesRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.User,
                AfterMessageId = 10,
                BeforeMessageId = 20
            };

            Assert.False(string.IsNullOrEmpty(request.Validate()));
        }

        [Fact]
        public async Task CreateGroup_写群与成员行_群主取登录身份()
        {
            var sut = Create();

            var result = await sut.Service.CreateGroupAsync(new CreateGroupRequest
            {
                Name = " 测试群 ",
                // 混入自身、0、重复值：都应被排除或去重
                MemberUserIds = [5, 5, 0, ContextUserId]
            });

            Assert.True(result.Code >= 200);

            var group = Assert.Single(sut.Groups.Created);
            Assert.Equal("测试群", group.Name);
            Assert.Equal(ContextUserId, group.OwnerUserId);

            Assert.Equal(2, sut.GroupMembers.AddedMembers.Count);
            Assert.Contains(sut.GroupMembers.AddedMembers, x => x.UserId == ContextUserId && x.MemberRole == 2);
            Assert.Contains(sut.GroupMembers.AddedMembers, x => x.UserId == 5 && x.MemberRole == 0);

            // 群主自己也要有一行会话，否则群不出现在他的列表里
            Assert.Equal(EmChatReceiverType.Group, sut.Sessions.LastReceiverType);
            Assert.Equal(group.Id, sut.Sessions.LastTargetId);
        }

        [Fact]
        public async Task CreateGroup_发成员变更事件_让在线成员进组()
        {
            // 不发这条事件，已在线的新成员要等重连才收得到群消息
            var sut = Create();

            await sut.Service.CreateGroupAsync(new CreateGroupRequest
            {
                Name = "告警群",
                MemberUserIds = [5]
            });

            var evt = Assert.Single(sut.Outbox.Enqueued.OfType<MembershipChangedEvent>());
            Assert.Equal(MembershipChangeType.Joined, evt.ChangeType);
            Assert.Equal(Assert.Single(sut.Groups.Created).Id, evt.GroupId);
            // 群主也带上：他的其他设备同样要进组
            Assert.Contains(ContextUserId, evt.UserIds);
            Assert.Contains(5, evt.UserIds);
        }

        [Fact]
        public async Task CreateGroup_群名为空_失败且不落库()
        {
            var sut = Create();

            var result = await sut.Service.CreateGroupAsync(new CreateGroupRequest { Name = "   " });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Groups.Created);
            Assert.Empty(sut.GroupMembers.AddedMembers);
        }

        [Fact]
        public async Task CreateGroup_成员超上限_失败且不落库()
        {
            var sut = Create();
            var members = Enumerable.Range(100, 500).Select(x => (long)x).ToList();

            var result = await sut.Service.CreateGroupAsync(new CreateGroupRequest
            {
                Name = "大群",
                MemberUserIds = members
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Groups.Created);
        }

        [Fact]
        public async Task AddGroupMembers_群主拉人_写成员并发入群事件()
        {
            var sut = Create();
            sut.Groups.Group = new EtGroupChat { Id = 50, OwnerUserId = ContextUserId, MaxMemberCount = 500 };
            sut.GroupMembers.Operator = new EtGroupChatMember { MemberRole = 2 };
            sut.GroupMembers.GroupMembers = [new EtGroupChatMember { UserId = ContextUserId, MemberRole = 2 }];

            var result = await sut.Service.AddGroupMembersAsync(new AddGroupMembersRequest
            {
                GroupId = 50,
                // 混入重复、0、自己和已存在成员
                UserIds = [5, 5, 0, ContextUserId]
            });

            Assert.True(result.Code >= 200);
            var added = Assert.Single(sut.GroupMembers.AddedMembers);
            Assert.Equal(5, added.UserId);
            Assert.Equal(0, added.MemberRole);

            var evt = Assert.IsType<MembershipChangedEvent>(Assert.Single(sut.Outbox.Enqueued));
            Assert.Equal(MembershipChangeType.Joined, evt.ChangeType);
            Assert.Equal(new long[] { 5 }, evt.UserIds);
        }

        [Fact]
        public async Task AddGroupMembers_普通成员且群禁止邀请_拒绝()
        {
            var sut = Create();
            sut.Groups.Group = new EtGroupChat { Id = 50, OwnerUserId = 99, AllowMemberInvite = false };
            sut.GroupMembers.Operator = new EtGroupChatMember { MemberRole = 0 };

            var result = await sut.Service.AddGroupMembersAsync(new AddGroupMembersRequest
            {
                GroupId = 50,
                UserIds = [5]
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.GroupMembers.AddedMembers);
            Assert.Empty(sut.Outbox.Enqueued);
        }

        [Fact]
        public async Task RemoveGroupMembers_踢人需要管理员权限()
        {
            var sut = Create();
            sut.Groups.Group = new EtGroupChat { Id = 50, OwnerUserId = 99 };
            sut.GroupMembers.Operator = new EtGroupChatMember { MemberRole = 0 };

            var result = await sut.Service.RemoveGroupMembersAsync(new RemoveGroupMembersRequest
            {
                GroupId = 50,
                UserIds = [5]
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.GroupMembers.Removed);
        }

        [Fact]
        public async Task RemoveGroupMembers_群主不能退群()
        {
            var sut = Create();
            sut.Groups.Group = new EtGroupChat { Id = 50, OwnerUserId = ContextUserId };
            sut.GroupMembers.Operator = new EtGroupChatMember { MemberRole = 2 };

            var result = await sut.Service.RemoveGroupMembersAsync(new RemoveGroupMembersRequest { GroupId = 50 });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.GroupMembers.Removed);
        }

        [Fact]
        public async Task RemoveGroupMembers_自己退群_发退群事件()
        {
            var sut = Create();
            sut.Groups.Group = new EtGroupChat { Id = 50, OwnerUserId = 99 };
            sut.GroupMembers.Operator = new EtGroupChatMember { MemberRole = 0 };

            var result = await sut.Service.RemoveGroupMembersAsync(new RemoveGroupMembersRequest { GroupId = 50 });

            Assert.True(result.Code >= 200);
            Assert.Equal((50L, ContextUserId), Assert.Single(sut.GroupMembers.Removed));

            var evt = Assert.IsType<MembershipChangedEvent>(Assert.Single(sut.Outbox.Enqueued));
            Assert.Equal(MembershipChangeType.Left, evt.ChangeType);
            Assert.Equal(new long[] { ContextUserId }, evt.UserIds);
        }

        [Fact]
        public async Task ListGroupMembers_非成员_拒绝()
        {
            var sut = Create();
            sut.GroupMembers.Operator = null;

            var result = await sut.Service.ListGroupMembersAsync(groupId: 50);

            Assert.False(result.Code >= 200);
        }

        [Fact]
        public async Task RecallMessage_自己发的_标记撤回并发事件()
        {
            var sut = Create();
            sut.Messages.Entry = new ChatMessageEntry
            {
                Id = 77,
                ReceiverType = EmChatReceiverType.User,
                FromUserId = ContextUserId,
                ToUserId = TargetId,
                SentAt = DateTime.UtcNow.AddSeconds(-10)
            };

            var result = await sut.Service.RecallMessageAsync(new RecallMessageRequest
            {
                ReceiverType = EmChatReceiverType.User,
                TargetId = TargetId,
                MessageId = 77
            });

            Assert.True(result.Code >= 200);
            Assert.Equal(77, Assert.Single(sut.Messages.Recalled).MessageId);

            var evt = Assert.IsType<MessageRecalledEvent>(Assert.Single(sut.Outbox.Enqueued));
            Assert.Equal(77, evt.MessageId);
            Assert.Equal(TargetId, evt.TargetId);
        }

        [Fact]
        public async Task RecallMessage_别人的消息_拒绝()
        {
            var sut = Create();
            sut.Messages.Entry = new ChatMessageEntry
            {
                Id = 77,
                ReceiverType = EmChatReceiverType.User,
                FromUserId = 99,
                ToUserId = TargetId,
                SentAt = DateTime.UtcNow
            };

            var result = await sut.Service.RecallMessageAsync(new RecallMessageRequest
            {
                ReceiverType = EmChatReceiverType.User,
                TargetId = TargetId,
                MessageId = 77
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Recalled);
        }

        [Fact]
        public async Task RecallMessage_超过时限_拒绝()
        {
            var sut = Create();
            sut.Messages.Entry = new ChatMessageEntry
            {
                Id = 77,
                ReceiverType = EmChatReceiverType.User,
                FromUserId = ContextUserId,
                ToUserId = TargetId,
                SentAt = DateTime.UtcNow.AddMinutes(-5)
            };

            var result = await sut.Service.RecallMessageAsync(new RecallMessageRequest
            {
                ReceiverType = EmChatReceiverType.User,
                TargetId = TargetId,
                MessageId = 77
            });

            Assert.False(result.Code >= 200);
            Assert.Empty(sut.Messages.Recalled);
        }

        [Fact]
        public async Task RecallMessage_已撤回_幂等且不重复发事件()
        {
            var sut = Create();
            sut.Messages.Entry = new ChatMessageEntry
            {
                Id = 77,
                ReceiverType = EmChatReceiverType.User,
                FromUserId = ContextUserId,
                ToUserId = TargetId,
                SentAt = DateTime.UtcNow,
                IsRecall = true
            };

            var result = await sut.Service.RecallMessageAsync(new RecallMessageRequest
            {
                ReceiverType = EmChatReceiverType.User,
                TargetId = TargetId,
                MessageId = 77
            });

            Assert.True(result.Code >= 200);
            Assert.Empty(sut.Messages.Recalled);
            Assert.Empty(sut.Outbox.Enqueued);
        }

        [Fact]
        public async Task GetPresence_按用户返回在线态_未知保持null()
        {
            var sut = Create();
            sut.Presence.States = new Dictionary<long, bool?> { [7] = true, [8] = false };

            var result = await sut.Service.GetPresenceAsync(new long[] { 7, 8, 9, 0, 7 });

            var typed = Assert.IsType<VivApiResult<List<PresenceItemOutput>>>(result);
            Assert.NotNull(typed.Data);

            // 0 与重复的 7 都被剔除
            Assert.Equal(new long[] { 7, 8, 9 }, typed.Data!.Select(x => x.UserId));
            Assert.True(typed.Data![0].Online);
            Assert.False(typed.Data![1].Online);
            Assert.Null(typed.Data![2].Online);
        }

        [Fact]
        public async Task ListSessions_合并单聊与群聊_置顶优先_其余按最后消息时间倒序()
        {
            var sut = Create();
            sut.Sessions.DirectSessions =
            [
                new EtChatSession { Id = 101, TargetUserId = 7, LastMessageTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new EtChatSession { Id = 102, TargetUserId = 8, LastMessageTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) }
            ];
            sut.GroupMembers.Memberships =
            [
                new EtGroupChatMember { GroupChatId = 50, IsTopSession = true, NoDisturb = true },
                new EtGroupChatMember { GroupChatId = 51 }
            ];
            sut.Groups.Groups =
            [
                new EtGroupChat { Id = 50, Name = "置顶群", LastMessageAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
                new EtGroupChat { Id = 51, Name = "闲置群" }
            ];
            sut.Messages.UnreadByTarget = new Dictionary<long, int> { [8] = 3 };

            var result = await sut.Service.ListSessionsAsync(pageIndex: 1, pageSize: 20);

            var typed = Assert.IsType<VivApiResult<List<ChatSessionItemOutput>>>(result);
            Assert.NotNull(typed.Data);

            // 置顶群 -> 1/3 的单聊 -> 1/1 的单聊 -> 没有消息的群
            Assert.Equal(
                new long[] { 50, 8, 7, 51 },
                typed.Data!.Select(x => x.TargetId));

            var top = typed.Data![0];
            Assert.Equal(EmChatReceiverType.Group, top.ReceiverType);
            Assert.Equal("置顶群", top.Name);
            Assert.True(top.IsTop);
            Assert.True(top.NoDisturb);

            // 未读按对端填充
            Assert.Equal(3, typed.Data![1].UnreadCount);
            Assert.Equal(0, typed.Data![2].UnreadCount);
        }

        [Fact]
        public async Task MarkRead_单聊_写自己那一行的水位()
        {
            var sut = Create();

            var result = await sut.Service.MarkReadAsync(new MarkReadRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.User,
                LastReadMessageId = 42
            });

            Assert.True(result.Code >= 200);
            Assert.Equal((TargetId, 42L), Assert.Single(sut.Sessions.LastReadWrites));
            Assert.Empty(sut.GroupMembers.LastReadWrites);
        }

        [Fact]
        public async Task MarkRead_群聊_写成员行的水位()
        {
            var sut = Create();

            var result = await sut.Service.MarkReadAsync(new MarkReadRequest
            {
                TargetId = TargetId,
                ReceiverType = EmChatReceiverType.Group,
                LastReadMessageId = 77
            });

            Assert.True(result.Code >= 200);
            Assert.Equal((TargetId, 77L), Assert.Single(sut.GroupMembers.LastReadWrites));
            Assert.Empty(sut.Sessions.LastReadWrites);
        }
    }
}
