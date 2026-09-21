namespace Viv.Momo.Tests;

/// <summary>
/// 显式开启才会跑：环境变量 VIV_CONTAINER_TESTS 为 1 或 true 时才拉起 Testcontainers，否则跳过。
/// 默认跳过而不是「有 Docker 就跑」，是因为 GitHub 的 ubuntu-latest 自带 Docker —— 按可用性判断等于每次 CI
/// 都去拉 SQL Server 镜像（约 450M）并等容器就绪，CI 被这几条集成测试拖着走。要跑时在本地显式开：
/// VIV_CONTAINER_TESTS=1 dotnet test src/Test/Viv.Momo.Tests
/// </summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerEnvironment.IsEnabled)
        {
            Skip = "容器集成测试未开启，跳过。要跑请设环境变量 VIV_CONTAINER_TESTS=1，并确保本机 Docker 可用。";
        }
    }
}

internal static class DockerEnvironment
{
    internal static bool IsEnabled { get; } = Detect();

    private static bool Detect()
        => Environment.GetEnvironmentVariable("VIV_CONTAINER_TESTS") is "1" or "true" or "TRUE";
}
