using System.Reflection;

namespace Viv.Fakes;

/// <summary>
/// <see cref="DispatchProxy"/> 替身基类 —— 原本散在各测试项目的 5 个 proxy 变体（两个
/// <c>ThrowingRedisProxy</c>、<c>CacheMissRedisProxy</c>、<c>ThrowingMessageBus</c>、
/// <c>CapturingMessageBus</c>）收成一个，用旋钮表达差异。
///
/// 这类替身只用 <c>DispatchProxy</c> 生成而不是手写：<c>IMomoDbContext</c> 有 55 个成员、
/// <c>IMessageBus</c> 有 10 个，而测试只关心其中一两个方法。
///
/// 不能加 sealed：<c>DispatchProxy.Create</c> 要派生一个动态类型出来，<c>TProxy</c>
/// 必须可跨程序集继承。本程序集里其它类都是 sealed，很容易顺手抄岔。
/// </summary>
public class TestProxy : DispatchProxy
{
    /// <summary>调用轨迹（方法名，按调用顺序）—— 「调没调过、调了几次」类断言的依据</summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// 最近一次调用的第一个实参 —— 顶替「记下最近一条被发布的消息」那种专用桩
    /// （单参数方法上它就是那个参数）。
    /// </summary>
    public object? LastArg { get; private set; }

    /// <summary>true = 任何调用都抛 <see cref="ThrowException"/></summary>
    public bool ThrowOnAnyCall { get; set; }

    /// <summary>抛什么；未设时抛 <see cref="InvalidOperationException"/></summary>
    public Exception? ThrowException { get; set; }

    /// <summary>
    /// 按声明返回类型指定回值，优先于默认回值 —— 用来表达「这个方法的真值不是 default」，
    /// 例如「取锁成功」：<c>Returns[typeof(Task&lt;bool&gt;)] = Task.FromResult(true)</c>。
    /// </summary>
    public Dictionary<Type, object?> Returns { get; } = [];

    /// <summary>建一个替身；<paramref name="configure"/> 里配旋钮（动态类型没法构造注入，只能事后配）。</summary>
    public static T Create<T>(Action<TestProxy>? configure = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        configure?.Invoke((TestProxy)(object)proxy);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod?.Name ?? "?");

        if (args is { Length: > 0 })
            LastArg = args[0];

        if (ThrowOnAnyCall)
            throw ThrowException ?? new InvalidOperationException("替身按配置抛出");

        var returnType = targetMethod?.ReturnType;

        return returnType is not null && Returns.TryGetValue(returnType, out var scripted)
            ? scripted
            : Default(returnType);
    }

    /// <summary>
    /// 按声明返回类型回一个「完成了的空值」。
    ///
    /// 原先 Momo 的 <c>NopProxy</c> 对 <c>Task&lt;T&gt;</c> 落进最后那行 <c>return null</c>，
    /// 一 await 就炸，只是那条路径碰巧没被走到。现在取的是更完备的那个实现
    /// （<c>CacheMissRedisProxy</c> 的 <c>Task.FromResult</c> 分支）。
    /// </summary>
    private static object? Default(Type? returnType)
    {
        if (returnType is null || returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return ValueTask.CompletedTask;

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var inner = returnType.GetGenericArguments()[0];
            var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;

            if (definition == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(inner)
                    .Invoke(null, [value]);

            if (definition == typeof(ValueTask<>))
                return Activator.CreateInstance(returnType, value);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}

/// <summary>
/// 纯空替身：任何调用都回「完成了的空值」，不抛也不记。
/// 等价于 <c>TestProxy.Create&lt;T&gt;()</c>，独立成类只为调用点读起来一眼知道「这里不需要它做事」。
///
/// 同样不能 sealed（见 <see cref="TestProxy"/> 的说明）。
/// </summary>
public class NopProxy : TestProxy;
