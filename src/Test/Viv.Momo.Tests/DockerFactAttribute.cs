namespace Viv.Momo.Tests;

/// <summary>
/// Docker 不可用时跳过，而不是失败。CI（ubuntu-latest 自带 Docker）会跑 Testcontainers；
/// 本地或无 Docker 的环境这是预期跳过。探测只看 docker.sock / DOCKER_HOST，不拉镜像。
/// </summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            Skip = "Docker 不可用，跳过 Testcontainers 集成测试。" +
                   "CI（GitHub ubuntu-latest）有 Docker 时会跑；本地无 Docker 时这是跳过不是失败。";
        }
    }
}

internal static class DockerEnvironment
{
    internal static bool IsAvailable { get; } = Detect();

    private static bool Detect()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return true;

        return File.Exists("/var/run/docker.sock");
    }
}
