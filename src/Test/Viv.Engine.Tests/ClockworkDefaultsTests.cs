using Viv.Clockwork;
using Viv.Clockwork.Options;
using Viv.Contracts.Models;
using Viv.Fakes;

namespace Viv.Engine.Tests;

public class ClockworkDefaultsTests
{
    [Fact]
    public void Dashboard启用无凭证_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClockworkExtensions.EnsureDashboardCredentials(new TickerQOptions
            {
                EnableDashboard = true,
                DashboardOptions = new TickerQDashboradOptions()
            }));

        Assert.Contains("凭证", ex.Message);
    }

    [Fact]
    public void Dashboard启用有用户名密码_通过()
    {
        ClockworkExtensions.EnsureDashboardCredentials(new TickerQOptions
        {
            EnableDashboard = true,
            DashboardOptions = new TickerQDashboradOptions
            {
                UserName = "viv",
                Password = "test-dashboard-pass"
            }
        });
    }

    [Fact]
    public void 已删除的EFCoreSchemaName不再存在()
    {
        Assert.Null(typeof(TickerQOptions).GetProperty("EFCoreSchemaName"));
    }

    [Fact]
    public void Dashboard启用有ApiKey_通过()
    {
        ClockworkExtensions.EnsureDashboardCredentials(new TickerQOptions
        {
            EnableDashboard = true,
            DashboardOptions = new TickerQDashboradOptions { WebApiKey = "key" }
        });
    }

    [Fact]
    public async Task 任务开始前没有快照_硬失败()
    {
        var scope = new RecordingLocalEventScope();
        var context = new TestContext();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => VivTickerJob.ExecuteAsync(scope, context, () => Task.CompletedTask));

        Assert.Contains("SetSnapshot", ex.Message);
        Assert.Equal(0, scope.RunCalls);
    }

    [Fact]
    public async Task 系统租户快照_成功Flush()
    {
        var scope = new RecordingLocalEventScope();
        var context = new TestContext();
        context.SetSnapshot(VivContextContent.ForSystemJob(11));

        await VivTickerJob.ExecuteAsync(scope, context, () => Task.CompletedTask);

        Assert.Equal(1, scope.RunCalls);
        Assert.Equal("flush", Assert.Single(scope.Bus.Calls));
    }

    [Fact]
    public async Task 任务体抛异常_Discard()
    {
        var scope = new RecordingLocalEventScope();
        var context = new TestContext();
        context.SetSnapshot(VivContextContent.ForSystemJob(11));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => VivTickerJob.ExecuteAsync(scope, context, () => throw new InvalidOperationException("job炸了")));

        Assert.Equal("discard", Assert.Single(scope.Bus.Calls));
    }
}
