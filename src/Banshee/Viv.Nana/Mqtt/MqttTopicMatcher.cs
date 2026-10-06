namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// MQTT topic 过滤器匹配（+ 单层通配、# 多层通配）。
    /// 不借 MQTTnet 的比较器：订阅是我们自己记住过滤器、自己分发，匹配语义就得自己说了算。
    /// </summary>
    internal static class MqttTopicMatcher
    {
        public static bool IsMatch(string topic, string filter)
        {
            if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(filter)) return false;

            var levels = topic.Split('/');
            var patterns = filter.Split('/');

            for (var i = 0; i < patterns.Length; i++)
            {
                // $ 开头的系统主题不被通配符匹配（MQTT 规范）
                if (i == 0 && patterns[0] is "#" or "+" && levels[0].StartsWith('$')) return false;

                if (patterns[i] == "#") return true;

                if (i >= levels.Length) return false;
                if (patterns[i] == "+") continue;
                if (!string.Equals(patterns[i], levels[i], StringComparison.Ordinal)) return false;
            }

            return patterns.Length == levels.Length;
        }
    }
}
