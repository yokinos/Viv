using System;

namespace Viv.Meta
{
    /// <summary>
    /// 标记特性：标注在类上，由 <c>Viv.Generators</c> 的生成器按字符串全名匹配，
    /// 为该类生成 partial 段（见 <see cref="IVivGenerated"/>）。
    /// 特性定义在宿主项目（本程序集），生成器不编译期引用它——避免 netstandard2.0 引用 net10.0 的兼容性问题。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class VivModelAttribute : Attribute
    {
        public VivModelAttribute(string tag = "")
        {
            Tag = tag;
        }

        /// <summary>可选标记位，生成代码可读取。</summary>
        public string Tag { get; }
    }

    /// <summary>生成器为标注 <see cref="VivModelAttribute"/> 的类型注入的标记接口。</summary>
    public interface IVivGenerated
    {
        string VivGeneratedMarker { get; }
    }
}
