using MagicOnion.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viv.Keke;

namespace Viv.Engine.Tests;

/// <summary>
/// MagicOnion 服务端装配被劈成两半：DI 半段在 <see cref="KekeRegister"/>（这里测），
/// 端点映射半段在 <c>RunVivApi</c>（要有真实宿主管道，口径同 <see cref="VivAppBuilderTests"/> —— 同进程只允许起一次宿主）。
/// 判据只有 <see cref="GrpcOptions.ServesMagicOnion"/> 一个，宿主不再自己读配置。
///
/// <para>断言用 <c>IConfigureOptions&lt;MagicOnionOptions&gt;</c> 而不是 <c>MagicOnionOptions</c> 本身：
/// <c>AddMagicOnion</c> 只注册选项配置器与内部的 marker service，不注册选项实例。</para>
/// </summary>
public class KekeRegisterTests
{
    private static IServiceCollection Register(KekeProtocol protocol)
    {
        var services = new ServiceCollection();
        KekeRegister.Initialize(services, new KekeOptions
        {
            EnableHttp = false,
            GrpcOption = new GrpcOptions { Protocol = protocol, Port = 7100 }
        });
        return services;
    }

    private static bool HasMagicOnion(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IConfigureOptions<MagicOnionOptions>));

    [Theory]
    [InlineData(KekeProtocol.MagicOnion, true)]
    [InlineData(KekeProtocol.Both, true)]
    [InlineData(KekeProtocol.Grpc, false)]
    [InlineData(KekeProtocol.None, false)]
    public void MagicOnion服务端只在协议含MagicOnion时装配(KekeProtocol protocol, bool expected)
    {
        Assert.Equal(expected, HasMagicOnion(Register(protocol)));
    }

    /// <summary>
    /// GrpcOption 为 null（纯 HTTP 宿主）时也不该冒出 MagicOnion 注册
    /// </summary>
    [Fact]
    public void 没有GrpcOption时不装配MagicOnion()
    {
        var services = new ServiceCollection();
        KekeRegister.Initialize(services, new KekeOptions { EnableHttp = true });

        Assert.False(HasMagicOnion(services));
    }
}
