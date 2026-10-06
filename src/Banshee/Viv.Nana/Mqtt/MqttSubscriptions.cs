using System.Collections.Concurrent;

namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// 订阅登记表。一次订阅有两个过滤器，**不能混**：
    /// <list type="bullet">
    /// <item><c>WireFilter</c> —— 下发给 broker 的那个（共享订阅带 $share/{group}/ 前缀），也是本表的 key</item>
    /// <item><c>TopicFilter</c> —— broker 推来的真实 topic 用这个匹配（**不带** $share 前缀）</item>
    /// </list>
    /// 混用的后果是共享订阅永远收不到消息（这两个过滤器曾共用一份，导致共享订阅完全失效）。
    /// </summary>
    internal sealed class MqttSubscriptions
    {
        /// <summary>一次订阅：两个过滤器 + 处理器</summary>
        internal sealed record Entry(string TopicFilter, string WireFilter, Func<MqttMessage, CancellationToken, Task> Handler);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        public int Count => _entries.Count;

        /// <summary>把业务给的 topic 过滤器拼成下发给 broker 的过滤器</summary>
        public static string ToWireFilter(string topicFilter, string? sharedGroup)
            => string.IsNullOrWhiteSpace(sharedGroup) ? topicFilter : $"$share/{sharedGroup}/{topicFilter}";

        /// <summary>登记一次订阅。同一个下发过滤器已存在时返回 false（调用方负责报错，别静默顶掉处理器）</summary>
        public bool TryAdd(string topicFilter, string? sharedGroup,
            Func<MqttMessage, CancellationToken, Task> handler, out Entry entry)
        {
            entry = new Entry(topicFilter, ToWireFilter(topicFilter, sharedGroup), handler);
            return _entries.TryAdd(entry.WireFilter, entry);
        }

        public bool Remove(string wireFilter) => _entries.TryRemove(wireFilter, out _);

        /// <summary>按**真实 topic** 找出匹配的订阅（用 TopicFilter 那一侧匹配）</summary>
        public List<Entry> Matching(string topic)
        {
            var matched = new List<Entry>();
            foreach (var entry in _entries.Values)
            {
                if (MqttTopicMatcher.IsMatch(topic, entry.TopicFilter)) matched.Add(entry);
            }
            return matched;
        }
    }
}
