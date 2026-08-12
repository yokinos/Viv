using Viv.Elysia.Attributes;
using Viv.Elysia.Extension;

namespace Viv.Elysia.Tests
{
    public class EnumNameTests
    {
        private enum TestEnum
        {
            [EnumName("正常")]
            Normal,

            Plain
        }

        [Fact]
        public void GetEnumName_WithAttribute_ReturnsName()
            => Assert.Equal("正常", TestEnum.Normal.GetEnumName());

        [Fact]
        public void GetEnumName_WithoutAttribute_ReturnsDefault()
            => Assert.Equal("", TestEnum.Plain.GetEnumName());

        [Fact]
        public void GetEnumName_CustomDefault_ReturnsCustom()
            => Assert.Equal("未知", TestEnum.Plain.GetEnumName("未知"));

        [Fact]
        public void GetEnumName_UndefinedValue_ReturnsDefault()
            => Assert.Equal("", ((TestEnum)999).GetEnumName());
    }
}
