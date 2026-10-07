using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Const
{
    /// <summary>
    /// 模型返回的结束原因常量。
    ///
    /// 刻意**不做成枚举**：这是模型厂商定义的**开放集合**（各家新增取值、甚至同一家换模型就多一个），
    /// 做成枚举后未知值要么解析失败、要么被静默降级成 Unknown，都会把"到底是什么"丢掉。
    /// 所以：这里只列已知取值供代码引用，**未知值一律原样入库**；
    /// 也不要拿它做业务分支（它只是记录，判断分支请用 <see cref="Enums.EmMessageRole"/> / <see cref="Enums.EmMessageContentType"/>）。
    /// </summary>
    public static class FinishReasons
    {
        /// <summary>
        /// 正常结束
        /// </summary>
        public const string Stop = "stop";

        /// <summary>
        /// 达到输出长度上限被截断
        /// </summary>
        public const string Length = "length";

        /// <summary>
        /// 模型要求调用工具（OpenAI 现行取值）
        /// </summary>
        public const string ToolCalls = "tool_calls";

        /// <summary>
        /// 模型要求调用函数（旧取值，兼容老数据）
        /// </summary>
        public const string FunctionCall = "function_call";

        /// <summary>
        /// 被内容过滤拦截
        /// </summary>
        public const string ContentFilter = "content_filter";
    }
}
