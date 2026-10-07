using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// <c>OtConfig</c>（全局键值配置表）的读取入口。**只读不写**：配置由运维/控制台落库，服务只消费。
    ///
    /// 键名沿用实体注释里给的示例写法（<c>分组.项</c>），值按 <c>OtConfig.ValueType</c> 解释。
    /// 读不到行、值为空、解析失败、库访问出错一律回**缺省值**并记 Warning，绝不抛 ——
    /// 一条脏配置不该把整轮对话打死。结果在内存里按 <see cref="IConfigVersionGate"/> 的版本戳失效。
    /// </summary>
    public interface IOuroborosConfig
    {
        /// <summary>
        /// 每个会话最多保留多少轮对话上下文（<c>OtConfig</c> 键 <c>Conversation.MaxRetainedTurns</c>）。
        /// 小于等于 0 表示不裁剪；缺省 20 轮。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> GetMaxRetainedTurnsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 单个工具结果交回模型前的最大字符数（<c>OtConfig</c> 键 <c>Tool.ResultMaxChars</c>）。
        /// 小于等于 0 表示不截断；缺省 8000 字符。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> GetToolResultMaxCharsAsync(CancellationToken cancellationToken = default);
    }
}
