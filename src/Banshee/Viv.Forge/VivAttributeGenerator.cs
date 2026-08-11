using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Viv.Forge
{
    /// <summary>
    /// 特性驱动的源生成器基类 —— 自动完成「扫描带指定特性的声明 → 匹配特性 → 交给子类提取」。
    /// 子类只需声明目标特性类型（TAttribute）并实现 <see cref="ExtractFromSymbol"/>。
    /// 具体生成器类请标注 <c>[Generator]</c>。
    /// </summary>
    /// <typeparam name="TAttribute">目标特性的元数据类型（用于从源码中按全名匹配，含命名空间校验）。</typeparam>
    /// <typeparam name="TInfo">提取出的编译期信息，交给 <see cref="VivSourceGenerator{TInfo}.Generate"/> 生成代码。</typeparam>
    public abstract class VivAttributeGenerator<TAttribute, TInfo> : VivSourceGenerator<TInfo>
        where TAttribute : Attribute
        where TInfo : class
    {
        /// <summary>
        /// 目标特性类型名（如 "GrpcClientAttribute"）。
        /// </summary>
        private static readonly string AttributeShortName = typeof(TAttribute).Name;

        /// <summary>
        /// 目标特性全名（如 "Viv.Echo.Grpc.GrpcClientAttribute"）。
        /// </summary>
        private static readonly string AttributeFullName = typeof(TAttribute).FullName ?? AttributeShortName;

        /// <summary>
        /// 目标特性所在命名空间（如 "Viv.Echo.Grpc"），防止同名特性跨命名空间误匹配。
        /// </summary>
        private static readonly string AttributeNamespace = typeof(TAttribute).Namespace ?? "";

        /// <summary>
        /// 候选节点 = 任何带特性列表的成员声明（类/接口/枚举/方法/属性等）。子类可继续收窄。
        /// </summary>
        protected virtual bool IsAttributeCandidate(SyntaxNode node)
            => node is MemberDeclarationSyntax { AttributeLists.Count: > 0 };

        protected sealed override bool IsCandidate(SyntaxNode node, CancellationToken ct)
            => IsAttributeCandidate(node);

        protected sealed override TInfo? Extract(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct);
            if (symbol is null) return null;

            foreach (var attr in symbol.GetAttributes())
            {
                if (IsTargetAttribute(attr))
                {
                    return ExtractFromSymbol(symbol, attr, ct);
                }
            }

            return null;
        }

        /// <summary>
        /// 子类实现：从带目标特性的符号 + 匹配到的特性中提取 <typeparamref name="TInfo"/>；返回 null 表示跳过。
        /// </summary>
        protected abstract TInfo? ExtractFromSymbol(ISymbol symbol, AttributeData attr, CancellationToken ct);

        private static bool IsTargetAttribute(AttributeData attr)
        {
            var cls = attr.AttributeClass;
            if (cls is null) return false;

            // 源码里 [GrpcClient] 与实际类型 GrpcClientAttribute 的短名/全名都要能命中
            if (cls.Name != AttributeShortName && cls.Name != AttributeFullName)
            {
                return false;
            }

            return cls.ContainingNamespace?.ToDisplayString() == AttributeNamespace;
        }
    }
}
