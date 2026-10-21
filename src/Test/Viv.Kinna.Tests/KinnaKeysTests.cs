using Viv.Kinna.Contracts;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// 用户 Key / 群名的生成规则。群名不带租户会让两个租户的 groupId 撞进同一个组。
    /// </summary>
    public class KinnaKeysTests
    {
        [Fact]
        public void GroupName_带租户前缀()
        {
            Assert.Equal("kinna:10:group:20", new KinnaKeys(null).GroupName(10, 20));
        }

        [Fact]
        public void GroupName_前缀来自配置()
        {
            // 一个进程托管两个 IM 域时，前缀必须各不相同，否则会串组
            Assert.Equal("herta:10:group:20", new KinnaKeys("herta").GroupName(10, 20));
        }

        [Fact]
        public void GroupName_前缀为空时回落默认值()
        {
            Assert.Equal("kinna:10:group:20", new KinnaKeys(" ").GroupName(10, 20));
        }

        [Fact]
        public void UserKey_带租户前缀()
        {
            Assert.Equal("kinna:10:user:7", new KinnaKeys(null).UserKey(10, 7));
        }
    }
}
