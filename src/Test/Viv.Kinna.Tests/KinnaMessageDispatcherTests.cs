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

        private static KinnaPushPlan UserPlan(
            string method = "M",
            long subjectId = SubjectId,
            long userId = UserId,
            long authorId = 0,
            bool mirrorToSender = false,
            bool skipSenderClient = true)
            => new(
                "user",
                method,
                "payload",
                KinnaPushTarget.ToUser(subjectId, userId),
                authorId,
                mirrorToSender,
                skipSenderClient);

        private static KinnaPushPlan GroupPlan(long groupId = 5, long subjectId = SubjectId, string method = "M")
            => new("group", method, "payload", KinnaPushTarget.ToGroup(subjectId, groupId));

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
        public async Task 群目标_组名由租户和群Id生成()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan(groupId: 5) };

            var outcome = await Create(pusher, sender, new StubKinnaConnectionPool()).DispatchAsync("evt", null);

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);
            Assert.Equal("Group", call.TargetKind);

            // 业务只给租户和群 Id，组名由 IKinnaKeys 拼——前缀和租户都在里面，跨租户串不了组
            Assert.Equal("kinna:11:group:5", call.TargetId);
        }

        [Fact]
        public async Task 群目标_缺租户或群Id_丢弃()
        {
            var sender = new RecordingKinnaClientSender();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan(groupId: 0) };

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
        public async Task 停机取消时不算推送失败_原样抛出()
        {
            // 主机停机时令牌被取消，SendAsync 抛 OCE。记成 failed 会让消费者去重投，
            // 而实际是没人再收这条消息；停机期间刷一片 failed 还会淹掉真故障
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var sender = new RecordingKinnaClientSender
            {
                ThrowOnSend = new OperationCanceledException(cts.Token)
            };

            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var logger = new RecordingLogger();
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan() };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => Create(pusher, sender, pool, logger).DispatchAsync("evt", null, cts.Token));

            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 令牌没取消时的取消异常仍算失败()
        {
            // 判别条件只看令牌：下游自己抛的 OCE（比如存储层超时）不是停机，
            // 照旧记 failed 让消费者重投
            var sender = new RecordingKinnaClientSender
            {
                ThrowOnSend = new OperationCanceledException("下游超时")
            };

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

        [Fact]
        public async Task 群播_把发起端从组里剔掉()
        {
            // 发起请求那台设备已经在接口返回里拿到了结果，再推一份就是同屏两条
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { AppConnectionIds = ["phone"] };
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan() };

            var outcome = await Create(pusher, sender, pool)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, UserId, null));

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            Assert.Equal("phone", Assert.Single(sender.Excluded));

            // 用的是信封里的 AppId：它才是「这个客户端」，连接 Id 不参与判断
            Assert.Equal(7, pool.LastAppId);
        }

        [Fact]
        public async Task 群播_没有信封时一个人都不剔()
        {
            // 没有身份就没法判断哪条是发起端，宁可多发一份也不能漏人
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { AppConnectionIds = ["phone"] };
            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => GroupPlan() };

            await Create(pusher, sender, pool).DispatchAsync("evt", null);

            Assert.Empty(sender.Excluded);
            Assert.Single(sender.Calls);
        }

        [Fact]
        public async Task 群播_关掉剔除开关时发起端照收()
        {
            // 发送方不用接口返回渲染气泡的客户端才需要这条：剔掉它自己就看不到消息了
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { AppConnectionIds = ["phone"] };
            var pusher = new FakeKinnaPusher<string>
            {
                OnPlan = (_, _) => GroupPlan() with { SkipSenderClient = false }
            };

            var outcome = await Create(pusher, sender, pool)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, UserId, null));

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            Assert.Empty(sender.Excluded);
        }

        [Fact]
        public async Task 单聊_补投发送方的其它端()
        {
            // 手机发出、PC 同步：目标除了接收方，还要并上署名人除发起端以外的连接
            const long receiverId = 22;
            const long authorId = 33;

            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool
            {
                ConnectionsByUser =
                {
                    [receiverId] = ["rcv"],
                    [authorId] = ["phone", "pc"]
                },
                AppConnectionsByUser = { [authorId] = ["phone"] }
            };

            var pusher = new FakeKinnaPusher<string>
            {
                OnPlan = (_, _) => UserPlan(userId: receiverId, authorId: authorId, mirrorToSender: true)
            };

            var outcome = await Create(pusher, sender, pool)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, authorId, null));

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);

            // 接收方 + 发送方的 PC，发起端（phone）被剔掉
            Assert.Equal("rcv,pc", call.TargetId);
        }

        [Fact]
        public async Task 单聊_自己发给自己_同一连接只发一次()
        {
            // 接收方和发送方是同一个人，两路查出来是同一批连接，必须按连接去重
            const long userId = 33;

            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool
            {
                ConnectionsByUser = { [userId] = ["phone", "pc"] },
                AppConnectionsByUser = { [userId] = ["phone"] }
            };

            var pusher = new FakeKinnaPusher<string>
            {
                OnPlan = (_, _) => UserPlan(userId: userId, authorId: userId, mirrorToSender: true)
            };

            await Create(pusher, sender, pool)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, userId, null));

            var call = Assert.Single(sender.Calls);
            Assert.Equal("pc", call.TargetId);
        }

        [Fact]
        public async Task 单聊_目标只剩发起端自己时算跳过()
        {
            // 那条连接已经拿过接口返回了，没有别人要发。判 Skipped 而不是 Failed，
            // 否则消费者会把「本来就没人可发」当成推送失败
            const long userId = 33;

            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool
            {
                ConnectionsByUser = { [userId] = ["phone"] },
                AppConnectionsByUser = { [userId] = ["phone"] }
            };

            var pusher = new FakeKinnaPusher<string> { OnPlan = (_, _) => UserPlan(userId: userId) };

            var outcome = await Create(pusher, sender, pool)
                .DispatchAsync("evt", new KinnaPushContext(7, SubjectId, userId, null));

            Assert.Equal(KinnaPushOutcome.Skipped, outcome);
            Assert.Empty(sender.Calls);
        }
    }
}
