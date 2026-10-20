using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    public class HertaLinkGroupsTests
    {
        [Fact]
        public void GetGroupName_带租户前缀()
        {
            // 租户必须在组名里，否则不同租户的 groupId 会撞进同一个组
            Assert.Equal("herta:10:group:20", HertaLinkGroups.GetGroupName(10, 20));
        }

        [Fact]
        public void GetGroupName_与HertaLinkKeys同源()
        {
            Assert.Equal(HertaLinkKeys.GetGroupName(10, 20), HertaLinkGroups.GetGroupName(10, 20));
        }

        [Fact]
        public void GetUserKey_带租户前缀()
        {
            Assert.Equal("herta:10:user:7", HertaLinkKeys.GetUserKey(10, 7));
        }
    }
}
