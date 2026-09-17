using System.Text.Json;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱 payload 的序列化选项 —— <b>自产自销，与 Wolverine 的序列化器无关</b>。
    ///
    /// <para>
    /// payload 的 JSON 由本模块 <c>Serialize</c>、也由本模块 <c>Deserialize</c>，
    /// Wolverine 从头到尾看不到这个字符串（它只看到投递时重新构造出来的那个信封对象）。
    /// 因此<b>完全不需要知道 Wolverine 用的是哪套 <c>JsonSerializerOptions</c></b>，
    /// 也就不存在「选项漂移 → 静默反序列化成默认值」这条风险路径。
    /// </para>
    /// </summary>
    internal static class OutboxJson
    {
        /// <summary>camelCase + 大小写不敏感，由一条 round-trip 测试钉住。</summary>
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
