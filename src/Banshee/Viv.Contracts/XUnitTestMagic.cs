using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts
{
    /// <summary>
    /// 单元测试辅助类，集中放置测试场景下常用的静态工具方法。
    /// </summary>
    /// <remarks>
    /// 项目中有大量名为 <c>Options</c> 的命名空间（如 <c>Viv.XXX.Options</c>），
    /// 直接书写 <c>Options.Create(...)</c> 容易产生歧义或冲突，
    /// 因此这里通过完全限定名统一封装，作为测试代码访问这些工具的无歧义入口。
    /// 后续与测试相关的通用辅助方法都可在此类中扩展。
    /// </remarks>
    public static class XUnitTestMagic
    {
        /// <summary>
        /// 将已有配置实例包装为 <see cref="Microsoft.Extensions.Options.IOptions{T}"/>，
        /// 便于在单元测试中直接构造被测对象所需的配置依赖。
        /// </summary>
        /// <typeparam name="T">配置类型，必须为引用类型。</typeparam>
        /// <param name="options">要包装的配置实例，不能为 null。</param>
        /// <returns>包装后的 <see cref="Microsoft.Extensions.Options.IOptions{T}"/> 实例。</returns>
        public static Microsoft.Extensions.Options.IOptions<T> CreateOptions<T>(T options) where T : class
        {
            return Microsoft.Extensions.Options.Options.Create(options);
        }
    }
}