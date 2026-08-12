using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Interface;
using Viv.Elysia.Request;

namespace Viv.Elysia.Tests
{
    public class RequestParameterValidatorTests
    {
        private static HashSet<object> NewSet() => new();

        // ── 基础分支 ─────────────────────────────────────────

        [Fact]
        public void Validate_NullObject_ReturnsNullObjectError()
            => Assert.Equal("校验对象不能为 null", RequestParameterValidator.Validate(null!, NewSet()));

        [Fact]
        public void Validate_SimpleType_ReturnsEmpty()
        {
            Assert.Equal("", RequestParameterValidator.Validate("text", NewSet()));
            Assert.Equal("", RequestParameterValidator.Validate(42, NewSet()));
            Assert.Equal("", RequestParameterValidator.Validate(DateTime.UtcNow, NewSet()));
            Assert.Equal("", RequestParameterValidator.Validate(TestEnum.Normal, NewSet()));
        }

        [Fact]
        public void Validate_NoValidationAttributes_ReturnsEmpty()
            => Assert.Equal("", RequestParameterValidator.Validate(new PlainModel(), NewSet()));

        // ── DataAnnotations 属性校验 ─────────────────────────

        [Fact]
        public void Validate_RequiredViolated_ReturnsError()
        {
            var error = RequestParameterValidator.Validate(new SampleModel(), NewSet());
            Assert.Contains("Name", error);
        }

        [Fact]
        public void Validate_RangeViolated_ReturnsError()
        {
            var error = RequestParameterValidator.Validate(new RangeModel(), NewSet());
            Assert.Contains("Age", error);
        }

        [Fact]
        public void Validate_StringLengthViolated_ReturnsError()
        {
            var error = RequestParameterValidator.Validate(new StringLengthModel { Name = new string('x', 100) }, NewSet());
            Assert.Contains("Name", error);
        }

        [Fact]
        public void Validate_AllValid_ReturnsEmpty()
        {
            var model = new SampleModel { Name = "abc", Age = 30 };
            Assert.Equal("", RequestParameterValidator.Validate(model, NewSet()));
        }

        // ── 类型级 / IValidatableObject ──────────────────────

        [Fact]
        public void Validate_TypeLevelAttributeViolated_ReturnsError()
        {
            var error = RequestParameterValidator.Validate(new TypeLevelModel(), NewSet());
            Assert.Contains("invalid", error);
        }

        [Fact]
        public void Validate_IValidatableObject_ReturnsSelfError()
        {
            var error = RequestParameterValidator.Validate(new ValidatableModel(), NewSet());
            Assert.Equal("self bad", error);
        }

        // ── 嵌套 / 集合 / 循环引用 ───────────────────────────

        [Fact]
        public void Validate_NestedComplexObject_ValidatedRecursively()
        {
            var holder = new NestedHolder { Inner = new InnerModel() };
            var error = RequestParameterValidator.Validate(holder, NewSet());
            Assert.Contains("Value", error);
        }

        [Fact]
        public void Validate_NestedIApiRequest_CallsItsValidate()
        {
            var holder = new RequestHolder { Request = new InnerRequest("inner bad") };
            Assert.Equal("inner bad", RequestParameterValidator.Validate(holder, NewSet()));
        }

        [Fact]
        public void Validate_CollectionElements_ValidatedRecursively()
        {
            var holder = new CollectionHolder { Items = { new InnerModel() } };
            var error = RequestParameterValidator.Validate(holder, NewSet());
            Assert.Contains("Value", error);
        }

        [Fact]
        public void Validate_CircularReference_DoesNotHang()
        {
            var a = new CyclicA { B = new CyclicB() };
            a.B.A = a;
            Assert.Equal("", RequestParameterValidator.Validate(a, NewSet()));
        }

        // ── DisplayName 优先级 ───────────────────────────────

        [Fact]
        public void Validate_DisplayNameAttribute_TakesPriority()
        {
            var error = RequestParameterValidator.Validate(new DisplayNameModel(), NewSet());
            Assert.Contains("姓名", error);
            Assert.DoesNotContain("Name", error);
        }

        // ── 异常分支 ─────────────────────────────────────────

        [Fact]
        public void Validate_PropertyGetterThrows_ReturnsReadFailure()
        {
            var error = RequestParameterValidator.Validate(new ThrowingModel(), NewSet());
            Assert.Contains("读取失败", error);
        }

        [Fact]
        public void Validate_ValidationAttributeThrows_ReturnsValidationFailure()
        {
            var error = RequestParameterValidator.Validate(new AttrThrowingModel(), NewSet());
            Assert.Contains("校验失败", error);
        }

        [Fact]
        public void Validate_WriteOnlyOrIndexerProperty_Skipped()
            => Assert.Equal("", RequestParameterValidator.Validate(new WeirdModel(), NewSet()));

        // ── 测试辅助类型 ─────────────────────────────────────

        private enum TestEnum { Normal }

        private sealed class PlainModel { public string Name { get; set; } = "x"; }

        private sealed class SampleModel
        {
            [Required]
            public string? Name { get; set; }

            [Range(1, 100)]
            public int Age { get; set; }
        }

        private sealed class RangeModel
        {
            [Range(1, 10)]
            public int Age { get; set; }
        }

        private sealed class StringLengthModel
        {
            [StringLength(10)]
            public string? Name { get; set; }
        }

        [TypeLevelValidation]
        private sealed class TypeLevelModel
        {
            public string Name { get; set; } = "x";
        }

        [AttributeUsage(AttributeTargets.Class)]
        private sealed class TypeLevelValidationAttribute : ValidationAttribute
        {
            public override bool IsValid(object? value) => false;
        }

        private sealed class ValidatableModel : IValidatableObject
        {
            public string Name { get; set; } = "x";

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                => new[] { new ValidationResult("self bad") };
        }

        private sealed class NestedHolder
        {
            public InnerModel? Inner { get; set; }
        }

        private sealed class InnerModel
        {
            [Required]
            public string? Value { get; set; }
        }

        private sealed class RequestHolder
        {
            public InnerRequest? Request { get; set; }
        }

        private sealed class InnerRequest : IApiRequest
        {
            private readonly string _error;
            public InnerRequest(string error) => _error = error;
            public string Validate() => _error;
        }

        private sealed class CollectionHolder
        {
            public List<InnerModel> Items { get; set; } = new();
        }

        private sealed class CyclicA { public CyclicB? B { get; set; } }

        private sealed class CyclicB { public CyclicA? A { get; set; } }

        private sealed class DisplayNameModel
        {
            [DisplayName("姓名")]
            [Required]
            public string? Name { get; set; }
        }

        private sealed class ThrowingModel
        {
            public string Explode => throw new InvalidOperationException("boom");
        }

        private sealed class AttrThrowingModel
        {
            [ThrowingValidation]
            public string Name { get; set; } = "x";
        }

        private sealed class ThrowingValidationAttribute : ValidationAttribute
        {
            public override bool IsValid(object? value) => throw new InvalidOperationException("boom");
        }

        private sealed class WeirdModel
        {
            public string? WriteOnly { set { } }
            public string this[int index] { get => "x"; set { } }
        }
    }
}
