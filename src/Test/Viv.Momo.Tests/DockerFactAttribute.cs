using Viv.Fakes;

namespace Viv.Momo.Tests;

/// <summary>
/// 容器集成测试的开关特性。判据与跳过文案都在 <see cref="ContainerTests"/>，
/// 这里只是 xUnit 的薄壳（特性必须继承 <c>FactAttribute</c>，而 Viv.Fakes 不带 xunit）。
///
/// 要跑：<c>VIV_CONTAINER_TESTS=1 dotnet test src/Test/Viv.Momo.Tests</c>
/// </summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!ContainerTests.IsEnabled)
        {
            Skip = ContainerTests.SkipReason;
        }
    }
}
