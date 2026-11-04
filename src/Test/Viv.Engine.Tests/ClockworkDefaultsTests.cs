using Viv.Clockwork;
using Viv.Clockwork.Options;
using Viv.Contracts.Interface;
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
    public async Task 任务开始前没有快照_硬失败且不执行任务体()
    {
        var context = new TestContext();
        var ran = false;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => VivTickerJob.RunAsync(context, () => { ran = true; return Task.CompletedTask; }));

        Assert.Contains("SetSnapshot", ex.Message);
        Assert.False(ran);
    }

    [Fact]
    public async Task 系统租户快照_正常执行任务体()
    {
        var context = new TestContext();
        context.SetSnapshot(VivContextContent.ForSystemJob(11));
        var ran = false;

        await VivTickerJob.RunAsync(context, () => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }

    [Fact]
    public async Task 任务体抛异常_原样上抛()
    {
        var context = new TestContext();
        context.SetSnapshot(VivContextContent.ForSystemJob(11));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => VivTickerJob.RunAsync(context, () => throw new InvalidOperationException("job炸了")));

        Assert.Equal("job炸了", ex.Message);
    }

    [Fact]
    public void 定时任务不再依赖本地事件作用域()
    {
        // 曾经基类和静态入口都要求 IVivLocalEventScope；定时任务默认只写库/发 MQ，
        // 这两条路的事务语义分别由工作单元和发件箱负责，不该强加这个依赖
        var ctor = typeof(VivTickerJobBase).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        Assert.All(ctor, c =>
            Assert.DoesNotContain(c.GetParameters(), p => p.ParameterType == typeof(IVivLocalEventScope)));
    }
}
