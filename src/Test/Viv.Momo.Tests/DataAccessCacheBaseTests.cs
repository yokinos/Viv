using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Fakes;
using Viv.Momo;

namespace Viv.Momo.Tests;

public class DataAccessCacheBaseTests
{
    [Fact]
    public async Task GetCacheAsync_Redis故障_回源数据库不抛连接异常()
    {
        var sut = new CacheSut(
            TestProxy.Create<IVivContext>(),
            TestProxy.Create<IMomoDbContext>(),
            CacheDoubles.ThrowingRedis(),
            new RecordingLogger());
        sut.DbValue = new TestBucket { Name = "from-db" };

        var result = await sut.GetCacheAsync(1);

        Assert.Equal("from-db", result?.Name);
        Assert.Equal(1, sut.DbCalls);
    }

    [Fact]
    public async Task GetCacheAsync_数据库故障_不误当成缓存降级不重查()
    {
        var sut = new CacheSut(
            TestProxy.Create<IVivContext>(),
            TestProxy.Create<IMomoDbContext>(),
            CacheDoubles.CacheMissRedis(),
            new RecordingLogger())
        {
            DbException = new VivConnectionException(VivConnType.SqlServer, "db down")
        };

        var ex = await Assert.ThrowsAsync<VivConnectionException>(() => sut.GetCacheAsync(1));

        Assert.Equal(VivConnType.SqlServer, ex.ConnType);
        Assert.Equal(1, sut.DbCalls);
    }
}
