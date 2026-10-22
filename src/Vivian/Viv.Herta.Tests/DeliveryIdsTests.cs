using Viv.Herta.Link.Delivery;

namespace Viv.Herta.Tests
{
    /// <summary>Redis 里 Id 列表的编解码。Lua 脚本按同一套标点规则拼，两边要对得上。</summary>
    public class DeliveryIdsTests
    {
        [Fact]
        public void Parse_空值返回空数组()
        {
            Assert.Empty(DeliveryIds.Parse(null));
            Assert.Empty(DeliveryIds.Parse(string.Empty));
            Assert.Empty(DeliveryIds.Parse("  "));
        }

        [Fact]
        public void Parse_去重并排序_忽略非法项()
        {
            var ids = DeliveryIds.Parse("12,11,12,abc,0,-3,13");

            Assert.Equal([11, 12, 13], ids);
        }

        [Fact]
        public void Format_只留正数并去重排序()
        {
            Assert.Equal("11,12", DeliveryIds.Format([12, 11, 12, 0, -1]));
            Assert.Equal(string.Empty, DeliveryIds.Format([]));
        }

        [Fact]
        public void 编解码往返一致()
        {
            long[] source = [3, 1, 2];

            Assert.Equal([1, 2, 3], DeliveryIds.Parse(DeliveryIds.Format(source)));
        }
    }
}
