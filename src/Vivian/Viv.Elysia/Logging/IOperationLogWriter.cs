using System.Threading;
using System.Threading.Tasks;

namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 操作日志的落地出口。<b>实现即接入</b>，不实现则由默认实现走事件（消息队列）。
    ///
    /// <para><b>为什么要有这一层</b>：原来日志只有"发 MQ 事件"一条路，于是"不配消息队列的服务"
    /// 与"想直接落库的服务"都被绑死在一个用不上的中间件上。抽出出口之后，落库 / 写文件 / 推 ES /
    /// 发 MQ 是并列的实现，框架只负责采集，不管日志去哪儿。</para>
    ///
    /// <para><b>可以同时挂多个</b>：容器里注册了几个实现就依次执行几个（MQ + 落库并存是常见诉求）。
    /// 每个实现独立捕获异常，一个写失败不影响另一个，更不影响请求结果。</para>
    ///
    /// <para><b>两种用法</b>：</para>
    /// <list type="bullet">
    /// <item><description>接口日志：注册进容器，过滤器采集完自动调用，业务代码不用管。</description></item>
    /// <item><description>主动记日志：Worker / 定时任务里直接注入调 <see cref="WriteAsync"/>，
    /// 补上了原来 <c>ElysiaLogContextAccessor.SetLog</c> 在没有 HttpContext 时"写了没人读"的空洞。</description></item>
    /// </list>
    /// </summary>
    public interface IOperationLogWriter
    {
        /// <summary>
        /// 写入一条操作日志。
        /// <para>实现方<b>不要抛业务异常</b>——调用侧虽有兜底，但日志写失败不该产生噪音；
        /// 确实需要上报的，自己记一条 Warning 即可。</para>
        /// </summary>
        /// <param name="record">采集好的日志。字段可能为 null（匿名接口没有 UserId、未开采集时没有 Body）。</param>
        /// <param name="cancellationToken">
        /// 调用侧传 <see cref="CancellationToken.None"/>：审计不该因为客户端断开而丢失。
        /// 实现方若要做超时控制，自己起 <c>CancellationTokenSource</c>。
        /// </param>
        ValueTask WriteAsync(OperationLogRecord record, CancellationToken cancellationToken = default);
    }
}
