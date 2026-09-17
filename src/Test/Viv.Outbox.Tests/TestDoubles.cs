using System.Text.Json;
using Viv.Contracts.Models;
using Viv.Nana;

namespace Viv.Outbox.Tests;

/// <summary>测试事件。必须继承 <see cref="NanaEvent"/> —— 发件箱投的就是跨进程那族。</summary>
public class OutboxTestEvent : NanaEvent
{
    public string Payload { get; set; } = string.Empty;

    public int Number { get; set; }
}

/// <summary>按 <c>OutboxStore</c> 的写法造一段 payload，供投递侧测试用。</summary>
public static class TestPayload
{
    /// <summary>
    /// 序列化选项必须与 <c>OutboxJson.Options</c> 一致 —— 这里刻意**另起一份**而不是抄它的实例：
    /// 两边各写各的，一旦哪天 Outbox 把 camelCase 去掉，这里就会解析不出来、测试立刻红。
    /// 共用同一个实例反而会把「选项漂移」这个要防的东西一起防掉。
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static string For<T>(T content, long messageId, VivContextContent? context = null) where T : NanaEvent
    {
        var envelope = new NanaEnvelope<T> { Content = content, MessageId = messageId, Context = context };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}
