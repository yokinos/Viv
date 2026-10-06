using Viv.Nana.Mqtt;

namespace Viv.Nana.Tests
{
    /// <summary>MQTT 接入的机制层：topic 匹配与订阅登记</summary>
    public class MqttTests
    {
        /// <summary>topic 过滤器匹配（+ 单层、# 多层、$ 系统主题）</summary>
        public class MqttTopicMatcherTests
        {
            [Theory]
            [InlineData("dev/abc/up/1", "dev/abc/up/1", true)]
            [InlineData("dev/abc/up/1", "dev/+/up/#", true)]
            [InlineData("dev/abc/up/1", "dev/+/up/1", true)]
            [InlineData("dev/abc/up/1", "dev/abc/+/#", true)]
            [InlineData("dev/abc/up/1", "#", true)]
            [InlineData("dev/abc/up/1", "dev/abc/down/#", false)]
            [InlineData("dev/abc/up/1", "dev/abc/up", false)]
            [InlineData("dev/abc/up", "dev/abc/up/1", false)]
            [InlineData("dev/abc/up/1", "dev/+/up", false)]
            [InlineData("$SYS/broker/uptime", "#", false)]
            [InlineData("$SYS/broker/uptime", "+/broker/uptime", false)]
            [InlineData("$SYS/broker/uptime", "$SYS/#", true)]
            [InlineData("", "dev/#", false)]
            [InlineData("dev/abc", "", false)]
            public void IsMatch_按MQTT规范匹配(string topic, string filter, bool expected)
                => Assert.Equal(expected, MqttTopicMatcher.IsMatch(topic, filter));
        }

        /// <summary>订阅登记：下发过滤器（带 $share）与匹配过滤器（不带）不能混 —— 混过一次，共享订阅完全失效</summary>
        public class MqttSubscriptionsTests
        {
            [Fact]
            public void 无共享组_下发的过滤器就是原样()
                => Assert.Equal("dev/+/up/#", MqttSubscriptions.ToWireFilter("dev/+/up/#", null));

            [Fact]
            public void 共享组为空串_视为没给()
                => Assert.Equal("dev/+/up/#", MqttSubscriptions.ToWireFilter("dev/+/up/#", "  "));

            [Fact]
            public void 带共享组_下发过滤器拼上share前缀()
                => Assert.Equal("$share/g1/dev/+/up/#", MqttSubscriptions.ToWireFilter("dev/+/up/#", "g1"));

            [Fact]
            public async Task 带共享组_真实topic仍能命中处理器()
            {
                var subscriptions = new MqttSubscriptions();
                var hit = new List<string>();

                Assert.True(subscriptions.TryAdd("dev/+/up/#", "g1",
                    (m, _) => { hit.Add(m.Topic); return Task.CompletedTask; }, out var entry));
                Assert.Equal("$share/g1/dev/+/up/#", entry.WireFilter);
                Assert.Equal("dev/+/up/#", entry.TopicFilter);

                // broker 推来的是真实 topic，不带 $share 前缀
                var matched = subscriptions.Matching("dev/abc/up/1");
                Assert.Single(matched);
                await matched[0].Handler(new MqttMessage { Topic = "dev/abc/up/1", Payload = [] }, CancellationToken.None);

                Assert.Equal("dev/abc/up/1", Assert.Single(hit));
            }

            [Fact]
            public void 无共享组_匹配用的是原过滤器()
            {
                var subscriptions = new MqttSubscriptions();
                Assert.True(subscriptions.TryAdd("dev/+/up/#", null, (_, _) => Task.CompletedTask, out _));

                Assert.Single(subscriptions.Matching("dev/abc/up/1"));
                Assert.Empty(subscriptions.Matching("dev/abc/down/1"));
            }

            [Fact]
            public void 同一个下发过滤器重复订阅_返回false()
            {
                var subscriptions = new MqttSubscriptions();

                Assert.True(subscriptions.TryAdd("dev/+/up/#", "g1", (_, _) => Task.CompletedTask, out _));
                Assert.False(subscriptions.TryAdd("dev/+/up/#", "g1", (_, _) => Task.CompletedTask, out _));
                Assert.Equal(1, subscriptions.Count);
            }

            [Fact]
            public void 同topic不同共享组_可以并存()
            {
                var subscriptions = new MqttSubscriptions();

                Assert.True(subscriptions.TryAdd("dev/+/up/#", "g1", (_, _) => Task.CompletedTask, out _));
                Assert.True(subscriptions.TryAdd("dev/+/up/#", "g2", (_, _) => Task.CompletedTask, out _));

                Assert.Equal(2, subscriptions.Count);
                Assert.Equal(2, subscriptions.Matching("dev/abc/up/1").Count);
            }

            [Fact]
            public void 移除后不再命中()
            {
                var subscriptions = new MqttSubscriptions();
                subscriptions.TryAdd("dev/+/up/#", "g1", (_, _) => Task.CompletedTask, out var entry);

                Assert.True(subscriptions.Remove(entry.WireFilter));
                Assert.False(subscriptions.Remove(entry.WireFilter));
                Assert.Empty(subscriptions.Matching("dev/abc/up/1"));
            }
        }
    }
}
