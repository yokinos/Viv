using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    /// <summary>
    /// 主体归属的取数口径：<see cref="AtUser"/> 上 OrgId / CompanyId / TenantId 是归属链，
    /// 哪一列代表「主体」由 UserType 决定。
    /// 这里锁住的是曾经的坑 —— CompanyUser 没有独立列可取时退回 TenantId，
    /// 结果 token 里装的是租户 Id，<c>CompanyAuthorizedAsync</c> 拿它去查公司表必然查不到。
    /// </summary>
    public class AtUserSubjectTests
    {
        [Fact]
        public void 主体按用户类型取归属链上对应的那一层()
        {
            var user = new AtUser { OrgId = 11, CompanyId = 22, TenantId = 33 };

            user.UserType = EmUserType.OrgUser;
            Assert.Equal(11, AtUser.SubjectIdOf(user));

            user.UserType = EmUserType.CompanyUser;
            Assert.Equal(22, AtUser.SubjectIdOf(user));

            user.UserType = EmUserType.TenantUser;
            Assert.Equal(33, AtUser.SubjectIdOf(user));

            user.UserType = EmUserType.EndUser;
            Assert.Equal(33, AtUser.SubjectIdOf(user));
        }

        [Fact]
        public void 主体缺失时返回0而不是退到别的层()
        {
            // 公司用户只填了租户：不能拿租户 Id 冒充公司 Id
            var user = new AtUser { TenantId = 33, UserType = EmUserType.CompanyUser };

            Assert.Equal(0, AtUser.SubjectIdOf(user));
        }

        [Fact]
        public void 未知用户类型返回0()
        {
            var user = new AtUser
            {
                OrgId = 11,
                CompanyId = 22,
                TenantId = 33,
                UserType = (EmUserType)200
            };

            Assert.Equal(0, AtUser.SubjectIdOf(user));
        }
    }
}
