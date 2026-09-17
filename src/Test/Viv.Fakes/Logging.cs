using Viv.Log;

namespace Viv.Fakes;

/// <summary>
/// 记录日志的内存替身 —— 全仓 <c>ILoggerContract</c> 桩的唯一实现（原本散在 9 个测试项目里的同名副本）。
///
/// 记录的是「填好参数的文本」：断言匹配的其实是模板里的字面量子串（"降级为回滚" / "回滚失败" /
/// "消息消费失败"），填不填参数都不影响它们，但填过之后测试失败时把 <see cref="Infos"/>
/// 打出来才看得懂。
///
/// <c>FormatException</c> 必须吞掉，这不是防御性编程：<c>VivExceptionFilterAttribute</c>
/// 用的是具名占位符（<c>"[全局异常] {Method} {Path} | RequestId: {RequestId}"</c>），
/// <c>string.Format</c> 碰到它必抛。替身不能因为被测代码的模板风格不同就把测试炸掉，
/// 回落成原文即可，反正那条断言只匹配字面量。
/// </summary>
public class RecordingLogger : ILoggerContract
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Error / Fatal —— 带异常的那条重载也记这里（Outbox 的「已吞掉」断言靠它）</summary>
    public List<string> Errors { get; } = [];

    /// <summary>只收带异常对象的 Error 重载，与 Message 配对 —— 用来确认「这条确实带着 InnerException」</summary>
    public List<(string Message, Exception? Ex)> ErrorWithException { get; } = [];

    public void Info(string message, params object[] args) => Infos.Add(Render(message, args));

    public void Debug(string message, params object[] args) { }

    public void Warning(string message, params object[] args) => Warnings.Add(Render(message, args));

    public void Error(string message, params object[] args) => Errors.Add(Render(message, args));

    public void Error(string message, Exception ex, params object[] args)
    {
        Errors.Add(Render(message, args));
        ErrorWithException.Add((Render(message, args), ex));
    }

    public void Fatal(string message, params object[] args) => Errors.Add(Render(message, args));

    public void Fatal(string message, Exception ex, params object[] args)
    {
        Errors.Add(Render(message, args));
        ErrorWithException.Add((Render(message, args), ex));
    }

    private static string Render(string message, object[] args)
    {
        if (args.Length == 0)
            return message;

        try
        {
            return string.Format(message, args);
        }
        catch (FormatException)
        {
            // 具名占位符（{Method}）或占位符与参数数量对不上 —— 记原文，别把测试炸了
            return message;
        }
    }
}
