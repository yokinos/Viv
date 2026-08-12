using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Tests
{
    public class ApexLoginRequestTests
    {
        private static ApexLoginRequest CreateValidRequest() => new()
        {
            AppId = 7,
            Version = 1000,
            UserName = "13800138000",
            Password = "pwd123",
            UserType = EmUserType.Master
        };

        [Fact]
        public void Validate_NonMasterWithoutSubjectCode_ReturnsLoginCodeError()
        {
            var request = CreateValidRequest();
            request.UserType = EmUserType.CompanyUser;
            request.SubjectCode = null;

            Assert.Equal("请携带对应的登录Code", request.Validate());
        }

        [Fact]
        public void Validate_NonMasterWithEmptySubjectCode_ReturnsLoginCodeError()
        {
            var request = CreateValidRequest();
            request.UserType = EmUserType.CompanyUser;
            request.SubjectCode = "";

            Assert.Equal("请携带对应的登录Code", request.Validate());
        }

        [Fact]
        public void Validate_NonMasterWithSubjectCodeAndValidFields_ReturnsEmpty()
        {
            var request = CreateValidRequest();
            request.UserType = EmUserType.CompanyUser;
            request.SubjectCode = "tenant-xyz";

            Assert.Equal(string.Empty, request.Validate());
        }

        [Fact]
        public void Validate_MasterWithoutSubjectCode_ReturnsEmpty()
        {
            var request = CreateValidRequest();

            Assert.Equal(string.Empty, request.Validate());
        }

        [Fact]
        public void Validate_MissingUserName_ReturnsAccountNameError()
        {
            var request = CreateValidRequest();
            request.UserName = null;

            Assert.Contains("账户名", request.Validate());
        }

        [Fact]
        public void Validate_MissingPassword_ReturnsPasswordError()
        {
            var request = CreateValidRequest();
            request.Password = null;

            Assert.Contains("密码", request.Validate());
        }

        [Fact]
        public void Validate_UserNameTooLong_ReturnsStringLengthError()
        {
            var request = CreateValidRequest();
            request.UserName = new string('a', 21);

            Assert.Contains("账户名", request.Validate());
        }

        [Fact]
        public void Validate_VersionOutOfRange_ReturnsVersionError()
        {
            var request = CreateValidRequest();
            request.Version = 0;

            Assert.Contains("服务器内部版本号", request.Validate());
        }
    }
}
