using Viv.Elysia.Request;

namespace Viv.Elysia.Tests
{
    public class ApiRequestBaseTests
    {
        private sealed class TestPagedRequest : ApiPagedRequestBase
        {
            public override (string sql, object parameters) GetSqlQuery() => ("", new object());
        }

        [Fact]
        public void Validate_EmptyRequest_ReturnsVersionRangeError()
        {
            // [Required] 标在值类型（long/int）上不生效——0 不是 null 直接放行；
            // 空请求实际命中 Version 的 [Range(1000,9999)]，Version=0 越界
            var error = new ApiEmptyRequest().Validate();
            Assert.Contains("服务器内部版本号", error);
        }

        [Fact]
        public void Validate_ValidAppIdButZeroVersion_ReturnsError()
            => Assert.NotEmpty(new ApiEmptyRequest { AppId = 1 }.Validate());

        [Fact]
        public void Validate_ValidRequest_ReturnsEmpty()
        {
            var request = new ApiEmptyRequest { AppId = 1, Version = 1000 };
            Assert.Equal("", request.Validate());
        }

        [Fact]
        public void Validate_PageIndexZero_ReturnsRangeError()
        {
            var error = new TestPagedRequest { AppId = 1, Version = 1000 }.Validate();
            Assert.Contains("当前页码", error);
        }

        [Fact]
        public void Validate_PageSizeOverMax_ReturnsRangeError()
        {
            var error = new TestPagedRequest { AppId = 1, Version = 1000, PageIndex = 1, PageSize = 10001 }.Validate();
            Assert.Contains("每页条数", error);
        }

        [Fact]
        public void Validate_ValidPagedRequest_ReturnsEmpty()
        {
            var request = new TestPagedRequest { AppId = 1, Version = 1000, PageIndex = 1, PageSize = 10 };
            Assert.Equal("", request.Validate());
        }
    }
}
