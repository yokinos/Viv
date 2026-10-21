using Viv.Fakes;
using Viv.Kinna.Dispatch;

namespace Viv.Kinna.Tests
{
    /// <summary>投递流程的几条规则：未命中算成功、失败不重投、代发只审计。</summary>
    public class KinnaMessageDispatcherTests
    {
        private const long SubjectId = 11;
        private const long UserId = 22;

        private static KinnaMessageDispatcher<TestHub, string> Create(
            FakeKinnaPusher<string> pusher,
            RecordingKinnaClientSender sender,
            StubKinnaConnectionPool pool,
            RecordingLogger? logger = null)
        {
            var session = new StubKinnaSession<TestHub>(pool, sender);
            return new KinnaMessageDispatcher<TestHub, string>(session, pusher, logger ?? new RecordingLogger());
        }

        private static KinnaPushPlan UserPlan(string method = "M", long subjectId = SubjectId, long userId = UserId)
            => new("user", method, "payload", KinnaPushTarget.ToUser(subjectId, userId));

        private static KinnaPushPlan GroupPlan(string groupName = "g1", string method = "M")
            => new("group", method, "payload", KinnaPushTarget.ToGroup(groupName));

        [Fact]
        public async Task 单聊目标_查到连接就发()
        {
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1", "c2"] };
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan() };

            var outcome = await Create(pusher, sender, pool).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);
            Assert.Equal("Clients", call.TargetKind);
            Assert.Equal("c1,c2", call.TargetId);
            Assert.Equal("payload", Assert.Single(call.Args));
            Assert.Equal(SubjectId, pool.LastSubjectId);
            Assert.Equal(UserId, pool.LastUserId);
        }

        [Fact]
        public async Task 单聊目标_不在本机是成功()
        {
            // 查不到就是人不在本机，判成失败会让消息进死信
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan() };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Skipped, outcome);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task 群目标_直接发组()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan("kinna:11:group:5") };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal("kinna:11:group:5", call.TargetId);
        }

        [Fact]
        public async Task 群目标_缺组名_丢弃()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan(string.Empty) };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Dropped, outcome);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task 单聊目标_缺租户或用户_丢弃()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan(subjectId: 0) };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Dropped, outcome);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task 计划为空_丢弃()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => null };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Dropped, outcome);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task 计划生成抛异常_失败且不外抛()
        {
            var sender = new RecordingKinnaClientSender();
            var logger = new RecordingLogger();
            var pusher = new FakeKinnaPusher<string> { ThrowOnPlan = new InvalidOperationException("boom") };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool(), logger)
                .DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Failed, outcome);
            Assert.Single(logger.Errors);
        }

        [Fact]
        public async Task 发送抛异常_失败且不外抛()
        {
            var sender = new RecordingKinnaClientSender { ThrowOnSend = new InvalidOperationException("boom") };
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var logger = new RecordingLogger();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan() };

            var outcome = await Create(pusher, sender, pool, logger).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Failed, outcome);
            Assert.Single(logger.Errors);
        }

        [Fact]
        public async Task 署名不是调用者_照常投递并留审计()
        {
            // 代发是正常场景：不拦，只留一条 Info
            var sender = new RecordingKinnaClientSender();
            var logger = new RecordingLogger();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var pusher = new FakeKinnaPusher<string>
            {
                OnPlan = (_, _) => UserPlan() with { AuthorId = 999 }
            };

            var outcome = await Create(pusher, sender, pool, logger)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, UserId, null));

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            Assert.Single(sender.Calls);
            Assert.Contains(logger.Infos, x => x.Contains("以他人名义"));
        }

        [Fact]
        public async Task 没有上下文时_照常按计划投递()
        {
            // 纯系统注入的事件没有信封，租户与署名全由计划决定
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan() };

            var outcome = await Create(pusher, sender, pool).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            Assert.Single(sender.Calls);
        }
    }
}
