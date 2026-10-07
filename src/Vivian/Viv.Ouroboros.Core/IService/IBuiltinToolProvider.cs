using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using Viv.Entity.Enums;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 内置工具（<see cref="EmToolTransport.Builtin"/>）的注册入口。
    ///
    /// 框架只提供这个入口，一个业务内置工具都不塞 —— 业务侧实现本接口 + <c>IDependency</c> 即被扫描注册，
    /// 注册表按 <c>OtTool.ToolKey</c> 查表；查不到就跳过该工具并记 Warning（不会让装配整体失败）。
    ///
    /// ⚠️ <b>实现必须是 Singleton，返回的委托里也不能捕获 Scoped 服务</b>：装配好的 Agent（连同工具闭包）
    /// 会被 AgentFactory 缓存 60 秒、跨请求复用，捕获 Scoped 实例就会在下一次请求用到已释放的对象。
    /// 需要按请求取库/取上下文时，注入 <c>IServiceScopeFactory</c> 并在调用内部新开作用域
    /// （<c>ToolCallRecorder</c> 就是这么做的）。
    /// </summary>
    public interface IBuiltinToolProvider
    {
        /// <summary>
        /// 按工具键取内置实现。返回的委托由 <see cref="IToolRegistry"/> 用工具定义里的名字与描述
        /// 交给 <c>AIFunctionFactory.Create</c> 包装，所以提供者不需要（也不该）自己关心工具名 ——
        /// 名字由实体决定，才不会被工厂按委托名生成成不可控的样子。
        /// </summary>
        /// <param name="toolKey">工具键（OtTool.ToolKey）</param>
        /// <param name="implementation">内置实现委托；没有则为 null</param>
        /// <returns>本提供者是否负责该工具键</returns>
        bool TryGetImplementation(string toolKey, out Delegate? implementation);
    }
}
