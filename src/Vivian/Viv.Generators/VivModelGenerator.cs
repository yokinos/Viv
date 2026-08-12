using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Viv.Forge;

namespace Viv.Generators
{
    /// <summary>
    /// 方案 A 样例生成器：字符串全名匹配 <c>Viv.Meta.VivModelAttribute</c>，
    /// 为标注类型生成 partial 段（声明实现 <c>Viv.Meta.IVivGenerated</c>）。
    /// 仅作「生成代码宿主」模型的端到端验证，业务生成器可据此替换。
    /// </summary>
    [Generator]
    public sealed class VivModelGenerator : VivSourceGenerator<VivModelInfo>
    {
        private const string AttributeFullName = "Viv.Meta.VivModelAttribute";

        protected override string GeneratorName => "VivModelGenerator";

        protected override IEqualityComparer<VivModelInfo>? Comparer => EqualityComparer<VivModelInfo>.Default;

        protected override bool IsCandidate(SyntaxNode node, CancellationToken ct)
            => node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0;

        protected override VivModelInfo? Extract(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax cds)
            {
                return null;
            }

            var symbol = ctx.SemanticModel.GetDeclaredSymbol(cds, ct);
            if (symbol is null || symbol.IsAbstract)
            {
                return null;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == AttributeFullName)
                {
                    return new VivModelInfo(symbol.Name, symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty);
                }
            }

            return null;
        }

        protected override void Generate(SourceProductionContext spc, ImmutableArray<VivModelInfo> infos, VivGeneratorContext ctx)
        {
            if (infos.IsDefaultOrEmpty)
            {
                return;
            }

            var sb = ctx.Source;
            foreach (var info in infos)
            {
                var ns = string.IsNullOrEmpty(info.Namespace) ? string.Empty : $"namespace {info.Namespace} ";
                sb.Line($"{ns}");
                sb.OpenBlock("");
                sb.Line($"public partial class {info.Name} : Viv.Meta.IVivGenerated");
                sb.OpenBlock("");
                sb.Line("public string VivGeneratedMarker => \"" + info.Name + "\";");
                sb.CloseBlock();
                sb.CloseBlock();
                sb.Line();
            }
        }
    }

    /// <summary>编译期提取的信息：类型名 + 所在命名空间（IEquatable 开启增量缓存）。</summary>
    public sealed class VivModelInfo : IEquatable<VivModelInfo>
    {
        public VivModelInfo(string name, string @namespace)
        {
            Name = name;
            Namespace = @namespace;
        }

        public string Name { get; }

        public string Namespace { get; }

        public bool Equals(VivModelInfo? other)
            => other is not null && Name == other.Name && Namespace == other.Namespace;

        public override bool Equals(object? obj) => Equals(obj as VivModelInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (Name?.GetHashCode() ?? 0);
                hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
