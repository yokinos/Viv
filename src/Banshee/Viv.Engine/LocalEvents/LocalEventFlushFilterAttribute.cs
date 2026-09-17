using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 本地事件分发触发点 —— HTTP 主路径，挂在 MVC 全局过滤链上，业务控制器无需标注。
    ///
    /// 这里用 action filter 而不是中间件，是因为两件事：
    /// 位置对 —— next() 返回时结果尚未执行、响应尚未写出，分发失败还能把响应改成错误信封；
    /// 看得见业务成败 —— VivExceptionFilterAttribute 在 next() 内部处理异常并置 ExceptionHandled，
    /// MVC 随之把已处理的异常从 ActionExecutedContext.Exception 上剥离，此时唯一的失败信号只剩
    /// context.Result 里那个错误 VivApiResult。中间件看不到信封码，只能看 HTTP 状态码，
    /// 而业务失败恰恰是「HTTP 200 + 信封非 2xx」，会被误判成成功。
    ///
    /// 分发不了的请求一律 Discard，不出现「库已经写坏了但事件照发」的幽灵事件。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class LocalEventFlushFilterAttribute : Attribute, IAsyncActionFilter
    {
        private readonly IVivLocalEventBus _localEventBus;

        public LocalEventFlushFilterAttribute(IVivLocalEventBus localEventBus)
        {
            _localEventBus = localEventBus;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // 异常未被处理时 next() 直接抛出，本过滤器后半段不会执行 —— 下面只处理
            // 「正常返回」与「异常已被 VivExceptionFilterAttribute 接住」两种情况
            var executed = await next();

            if (IsFailed(executed))
            {
                _localEventBus.Discard();
                return;
            }

            // 刻意传 None、不用 HttpContext.RequestAborted：本地事件的 handler 就是业务的一部分，
            // 写下来就必须跑完。客户端中途断开不该造成「主业务已提交、通知没发出去」的脱节。
            // 真能容忍不执行的逻辑，一开始就不该用本地事件，该走 MQ。
            await _localEventBus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 本次请求是否算业务失败（失败则整队丢弃，一条事件都不发）。
        ///
        /// 信封那一段直接复用 <see cref="FailDetector"/>，与工作单元的提交/回滚判定同一条规则。
        /// 以前这里抄了一份 Code&lt;200 || Code&gt;=300，靠反射测试盯着两份不漂移，复用之后不需要了。
        /// </summary>
        private static bool IsFailed(ActionExecutedContext executed)
        {
            // 异常已被异常过滤器接住 → 失败
            if (executed.ExceptionHandled)
                return true;

            // 未接住的异常不会被判到这里（next() 已经抛出），兜底判一次以防过滤链行为变化
            if (executed.Exception != null)
                return true;

            // 业务自己返回失败信封：HTTP 仍是 200，成败只能看信封码
            return FailDetector.IsFailed(executed.Result);
        }
    }
}
