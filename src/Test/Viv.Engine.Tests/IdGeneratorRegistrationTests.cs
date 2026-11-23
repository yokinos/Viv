using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Delusion.Magic;
using Viv.Engine.Options;

namespace Viv.Engine.Tests;

/// <summary>
/// ID 生成器装配 —— 机器号必须从 EnvOption 走通。
///
/// 这一组测试守的是「所有宿主机器段都掉成同一个值」那个回归：
/// 装配前 IdMagic 的机器号写死 1，各宿主 appsettings 里的 MachineId 只用于渲染欢迎页，
/// 结果任意两个进程同毫秒各发一粒 ID 就得到完全相同的 long，而且没有任何报错。
/// </summary>
[Collection("VivEngineStaticState")]
public class IdGeneratorRegistrationTests
{
    /// <summary>默认位宽下机器段的位置：序列 12 位，机器段 10 位。</summary>
    private const int SequenceBits = 12;
    private const long MachineIdMask = 1023L;

    [Fact]
    public void 机器段取自EnvOption的MachineId()
    {
        var id = BuildGenerator(7).NextId();

        Assert.Equal(7L, (id >> SequenceBits) & MachineIdMask);
    }

    [Fact]
    public void 不同宿主产出不同机器段()
    {
        // 同一毫秒内两个宿主各发一粒 —— 时间戳段相同、序列都从 0 起，
        // 机器段是唯一能把这粒 ID 区分开的东西
        var fromApex = BuildGenerator(2).NextId();
        var fromHerta = BuildGenerator(4).NextId();

        Assert.NotEqual((fromApex >> SequenceBits) & MachineIdMask, (fromHerta >> SequenceBits) & MachineIdMask);
        Assert.NotEqual(fromApex, fromHerta);
    }

    [Fact]
    public void 装配同时绑定IdMagic默认实例()
    {
        var generator = BuildGenerator(5);

        // VivCallContext 与消息信封拿不到容器，只能走静态门面；
        // 若两者指向不同实例，进程内就各跑各的序列，机器号相同的问题会原样重演
        Assert.Same(generator, IdMagic.Default);
        Assert.True(IdMagic.IsDefaultBound);
    }

    [Fact]
    public void 锁持有者Id跟随宿主机器号()
    {
        // 以前锁持有者拿的是专用机器号 1022，与宿主自己的号分属两个序列 ——
        // 同一毫秒里两粒 ID 各自从 0 起，撞不撞全看运气。现在必须用同一个生成器
        BuildGenerator(6);

        var holderId = long.Parse(VivCallContext.GenerateHolderId());

        Assert.Equal(6L, (holderId >> SequenceBits) & MachineIdMask);
    }

    private static Viv.Delusion.Snowflake.IIdGenerator BuildGenerator(int machineId)
    {
        var options = new VivOptions { EnvOption = new EnvOptions { MachineId = machineId } };
        var services = new ServiceCollection();

        VivRegister.RegisterIdGenerator(services, options);

        return services.BuildServiceProvider().GetRequiredService<Viv.Delusion.Snowflake.IIdGenerator>();
    }
}
