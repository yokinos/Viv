using System.Reflection;
using Viv.Momo;

namespace Viv.Momo.Tests;

/// <summary>
/// 接口契约：所有返回 Task 的方法都要能取消。
///
/// 以前是「一半有一半没有」——调用方按名字猜不出哪个能取消，补的时候也没个准绳。
/// 这里把规则钉死：新加异步方法忘了带 CancellationToken，测试直接红。
/// </summary>
public class CancellationTokenContractTests
{
    /// <summary>接口上所有返回 Task / Task&lt;T&gt; 的方法</summary>
    private static List<MethodInfo> AsyncMethods() =>
        typeof(IMomoDbContext).GetMethods()
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .ToList();

    [Fact]
    public void 返回Task的方法全部带CancellationToken()
    {
        var methods = AsyncMethods();

        // 扫不到方法就恒绿 —— 接口被改名 / 反射写法失效时要炸出来，不能静默通过
        Assert.NotEmpty(methods);

        var missing = methods
            .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            .Select(Describe)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// 必须是最后一个参数且可选。
    ///
    /// 最后一个是硬要求：放在 <c>object? parameters</c> 之前会把
    /// <c>SingleOrDefaultAsync&lt;T&gt;(sql, parameters)</c> 这类位置传参全部打挂。
    /// 可选是为了不影响既有调用点。
    /// </summary>
    [Fact]
    public void CancellationToken必须是最后一个参数且可选()
    {
        var offenders = new List<string>();

        foreach (var method in AsyncMethods())
        {
            var parameters = method.GetParameters();
            var index = Array.FindIndex(parameters, p => p.ParameterType == typeof(CancellationToken));
            if (index < 0) continue;   // 没带，由另一条测试负责报

            if (index != parameters.Length - 1) offenders.Add($"{Describe(method)} —— 不在最后");
            if (!parameters[index].HasDefaultValue) offenders.Add($"{Describe(method)} —— 没有默认值");
        }

        Assert.Empty(offenders);
    }

    private static string Describe(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";
}
