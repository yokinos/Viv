using Viv.Contracts.Models;
using Viv.Sandrone.Impl;

namespace Viv.Sandrone.Tests;

/// <summary>
/// VivContextAccessor —— AsyncLocal 存放的唯一位置。
/// 验证首次访问生成、同执行流复用、可清除、可流入 Task.Run（与 Redis LockHolderContext 同构）。
/// </summary>
public class VivContextAccessorTests
{
    [Fact]
    public void 首次访问为null_设置后读取()
    {
        var accessor = new VivContextAccessor();
        Assert.Null(accessor.Current);

        accessor.Current = new VivContextContent { UserId = 1 };
        Assert.NotNull(accessor.Current);
        Assert.Equal(1, accessor.Current!.UserId);

        accessor.Current = null;
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void 同执行流复用同一实例()
    {
        var accessor = new VivContextAccessor();
        accessor.Current = new VivContextContent { AppId = 5 };

        Assert.Same(accessor.Current, accessor.Current); // 同一 AsyncLocal 槽位
        Assert.Equal(5, accessor.Current!.AppId);
    }

    [Fact]
    public async Task 上下文流入TaskRun()
    {
        var accessor = new VivContextAccessor();
        accessor.Current = new VivContextContent { UserId = 9 };

        var flowed = await Task.Run(() => accessor.Current?.UserId);

        Assert.Equal(9, flowed);
    }

    [Fact]
    public async Task 清除后TaskRun不再继承()
    {
        var accessor = new VivContextAccessor();
        accessor.Current = new VivContextContent { UserId = 9 };
        accessor.Current = null;

        var flowed = await Task.Run(() => accessor.Current?.UserId);

        Assert.Null(flowed);
    }
}
