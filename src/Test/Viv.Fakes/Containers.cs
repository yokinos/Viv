namespace Viv.Fakes;

/// <summary>
/// Testcontainers 集成测试的开关判据 —— **判据只此一份**，各测试项目的 <c>[DockerFact]</c> 只是薄壳。
///
/// 为什么特性本身不在这里：<c>DockerFactAttribute</c> 要继承 xUnit 的 <c>FactAttribute</c>，
/// 而 Viv.Fakes 刻意不带 xunit（带了 <c>dotnet test</c> 会把它当测试宿主去跑，报「没有可用测试」）。
/// 所以特性留在各自的测试项目里，把「看哪个环境变量、跳过时说什么」集中到这里 ——
/// 之前 Engine / Momo 两份是靠注释互相提醒「要保持一致」的，那是靠人记。
///
/// 默认跳过而不是「有 Docker 就跑」：GitHub 的 ubuntu-latest 自带 Docker，按可用性判断等于
/// 每次 CI 都去拉 SQL Server 镜像（约 450M）并等容器就绪，CI 被这几条集成测试拖着走。
/// </summary>
public static class ContainerTests
{
    /// <summary>要跑容器测试就把它设成 1 / true。</summary>
    public const string ToggleVariable = "VIV_CONTAINER_TESTS";

    /// <summary>没开时 <c>Skip</c> 显示的原因。</summary>
    public const string SkipReason =
        "容器集成测试未开启，跳过。要跑请设环境变量 VIV_CONTAINER_TESTS=1，并确保本机 Docker 可用。";

    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable(ToggleVariable) is "1" or "true" or "TRUE";
}
