namespace Viv.Keke.Tests;

/// <summary>
/// 会读写 <see cref="Viv.Contracts.VivCallContext"/> 的测试串行跑。
///
/// <para>
/// 身份头构造从 <c>VivCallContext</c> 取 holderId 与 traceId（不是从上下文快照取），
/// 它是进程级 AsyncLocal —— 并行时一个测试设的 traceId 会被另一个测试读到，
/// 断言随机失败且看不出原因。
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KekeStaticStateCollection
{
    public const string Name = "Keke 静态状态";
}
