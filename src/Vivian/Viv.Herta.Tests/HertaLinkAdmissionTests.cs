using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    /// <summary>
    /// 准入分两步：先看身份齐不齐，再问 Apex 这个 AppId 是不是启用的客户端。
    /// 身份不齐时不该白跑一趟 gRPC。
    /// </summary>
    public class HertaLinkAdmissionTests
    {
        private static HertaLinkAdmission Create(StubClientAppQuery apps) => new(apps);

        private static VivContextContent Identity(long appId = 1, long subjectId = 2, long userId = 3)
            => new() { AppId = appId, SubjectId = subjectId, UserId = userId };

        [Fact]
        public async Task Apex说是有效客户端_放行()
        {
            var apps = new StubClientAppQuery { AllowAll = true };

            var check = await Create(apps).CheckAsync(Identity(appId: 7));

            Assert.True(check.Allowed);
            Assert.Equal(7, apps.LastAppId);
        }

        [Fact]
        public async Task Apex说不存在_拒绝并带上原因()
        {
            var apps = new StubClientAppQuery { Reason = "客户端不存在" };

            var check = await Create(apps).CheckAsync(Identity(appId: 9));

            Assert.False(check.Allowed);
            Assert.Contains("客户端不存在", check.Reason);
        }

        [Fact]
        public async Task Apex说已禁用_拒绝()
        {
            var apps = new StubClientAppQuery { Reason = "客户端已禁用" };

            var check = await Create(apps).CheckAsync(Identity(appId: 9));

            Assert.False(check.Allowed);
            Assert.Contains("禁用", check.Reason);
        }

        [Fact]
        public async Task 没有身份_拒绝且不问Apex()
        {
            var apps = new StubClientAppQuery { AllowAll = true };

            var check = await Create(apps).CheckAsync(null);

            Assert.False(check.Allowed);
            Assert.Contains("身份", check.Reason);
            Assert.Equal(0, apps.Calls);
        }

        [Fact]
        public async Task 缺AppId_拒绝且不问Apex()
        {
            var apps = new StubClientAppQuery { AllowAll = true };

            var check = await Create(apps).CheckAsync(Identity(appId: 0));

            Assert.False(check.Allowed);
            Assert.Contains("AppId", check.Reason);
            Assert.Equal(0, apps.Calls);
        }

        [Fact]
        public async Task 身份不全_拒绝且不问Apex()
        {
            var apps = new StubClientAppQuery { AllowAll = true };

            Assert.False((await Create(apps).CheckAsync(Identity(subjectId: 0))).Allowed);
            Assert.False((await Create(apps).CheckAsync(Identity(userId: 0))).Allowed);
            Assert.Equal(0, apps.Calls);
        }
    }
}
