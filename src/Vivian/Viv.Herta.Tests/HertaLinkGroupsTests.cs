using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    public class HertaLinkGroupsTests
    {
        [Fact]
        public void GetGroupName_FormatsGroupColonTenantColonGroup()
            => Assert.Equal("group:10:20", HertaLinkGroups.GetGroupName(10, 20));
    }
}
