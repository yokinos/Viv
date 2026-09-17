using System.Text.Json;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱 payload 的序列化选项，自产自销，与 Wolverine 的序列化器无关。
    ///
    /// payload 由本模块序列化、也由本模块反序列化，Wolverine 看不到这个字符串，
    /// 因此不需要知道它用的是哪套选项，也就不存在「选项漂移 → 静默反序列化成默认值」这条路径。
    /// </summary>
    internal static class OutboxJson
    {
        /// <summary>camelCase + 大小写不敏感，由一条 round-trip 测试钉住。</summary>
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
