using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Elysia.Attributes;
using Viv.Elysia.Logging;
using Viv.Elysia.Security;
using Viv.Engine;
using Viv.Entity.Enums;
using Viv.Log;

namespace Viv.Elysia.Filter
{
    /// <summary>
    /// 操作日志采集器。职责只有四件：<b>判定要不要记</b> → <b>采集</b> → <b>组装</b> → <b>交给出口</b>。
    /// 写到哪里由 <see cref="IOperationLogWriter"/> 决定，这里不认识 MQ、也不认识数据库。
    ///
    /// <para><b>和旧实现的差别，逐条对应修掉的问题</b>：</para>
    /// <list type="bullet">
    /// <item><description>请求参数取 <c>ActionArguments</c>（模型绑定后的强类型对象），不再
    /// <c>EnableBuffering()</c> 重读原始流。旧做法在 action 过滤器里执行，那时 body 早已被 JSON
    /// 格式化器读完、位置在 EOF，重读回来恒为空——只有测试里手工塞 <c>MemoryStream</c> 才"绿"。</description></item>
    /// <item><description>响应取 <c>executed.Result</c> 对象本身，不回显 <c>Response.Body</c>。
    /// 旧实现把空的 <c>MemoryStream</c> 挂上去再读回来，恒为 <c>""</c>。</description></item>
    /// <item><description>日志正文取特性的 <c>Name</c>（或业务 <c>Describe</c>），不取接口返回的 Message。</description></item>
    /// <item><description>失败默认也记：<c>RecordFailure</c> 默认 true。审计最需要留痕的是失败尝试。</description></item>
    /// <item><description>出口失败只记 Warning，绝不改变请求结果；旧实现在 action 成功后抛异常 → 前端收到 500。</description></item>
    /// </list>
    /// </summary>
    internal sealed class OperationLogFilter : IAsyncActionFilter
    {
        /// <summary>
        /// 序列化设置：忽略循环引用（DTO 里带父引用不至于让整条日志丢失）、跳过 null（体积）。
        /// 用 System.Text.Json 而不是 Newtonsoft：日志是自用格式，不需要那套兼容特性。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IEnumerable<IOperationLogWriter> _writers;
        private readonly IVivContext _vivContext;
        private readonly OperationLogOptions _options;

        public OperationLogFilter(
            IEnumerable<IOperationLogWriter> writers,
            IVivContext vivContext,
            IServiceProvider services)
        {
            _writers = writers;
            _vivContext = vivContext;

            // 没装 AddVivOperationLog 时退化成默认策略（有特性、无出口，每请求一条 Warning），
            // 而不是让容器解析失败把每个请求打成 500 —— 旧实现就是因为构造函数硬依赖发布器，
            // 让"不配消息队列的服务 + AddElysiaFilter"每个请求都 500。
            _options = services.GetService<OperationLogOptions>() ?? new OperationLogOptions();
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // 包先建好：action 里的 IOperationLogScope 要往里写，两条路拿的必须是同一个实例
            var bag = OperationLogBag.Ensure(context.HttpContext);
            var started = Stopwatch.GetTimestamp();

            ActionExecutedContext executed;
            try
            {
                executed = await next().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 异常过滤器在 action 过滤器外层，接不到这段 —— 走到这里说明异常要往上抛了。
                // 记一条失败日志再原样抛（throw 保留调用栈），请求结果不变。
                await EmitAsync(context, bag, started, executed: null, exception: ex).ConfigureAwait(false);
                throw;
            }

            await EmitAsync(context, bag, started, executed, exception: null).ConfigureAwait(false);
        }

        private async ValueTask EmitAsync(
            ActionExecutingContext context,
            OperationLogBag bag,
            long startedTimestamp,
            ActionExecutedContext? executed,
            Exception? exception)
        {
            if (!_options.Enabled || bag.Skip)
            {
                return;
            }

            var attribute = (context.ActionDescriptor as ControllerActionDescriptor)?
                .MethodInfo.GetCustomAttribute<OperationLogAttribute>(inherit: true);

            // opt-in：没标注、业务也没碰过 Scope（既没 Target 也没 Describe）→ 这不是一次被声明的业务操作
            if (attribute is null && !bag.Touched)
            {
                return;
            }

            var result = executed?.Result as VivApiResult;

            // executed.Exception 与上面 catch 到的是同一件事的两条路径（取决于异常是否被处理过），
            // 这里统一看成"失败"，避免异常被吞掉的情形记成成功
            var failure = exception ?? executed?.Exception;
            var succeeded = failure is null && (result is null || result.Code == (int)ApiResultCode.Success);

            if (!succeeded && !_options.RecordFailure)
            {
                return;
            }

            var http = context.HttpContext;
            var operation = attribute?.Operation;

            var record = new OperationLogRecord
            {
                Module = ResolveModule(context),
                Operation = operation,
                Name = attribute?.Name,
                // 正文优先级：业务 Describe > 特性 Name > 操作类型的中文名（"新增"/"修改"…）
                Description = bag.Description
                              ?? attribute?.Name
                              ?? (operation is null ? null : operation.Value.GetDescription()),
                TargetType = bag.TargetType,
                TargetId = bag.TargetId,
                UserId = PositiveOrNull(_vivContext.UserId),
                SubjectId = PositiveOrNull(_vivContext.SubjectId),
                AppId = PositiveOrNull(_vivContext.AppId),
                ResultCode = result?.Code ?? (failure is null ? (int)ApiResultCode.Success : (int)ApiResultCode.ServerError),
                Succeeded = succeeded,
                DurationMs = (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                TraceId = string.IsNullOrEmpty(_vivContext.TraceId) ? http.TraceIdentifier : _vivContext.TraceId,
                Path = http.Request.Path.Value,
                Ip = http.Connection.RemoteIpAddress?.ToString(),
                UserAgent = http.Request.Headers.UserAgent.ToString(),
                RequestJson = _options.IncludeRequest ? Sanitize(SerializeArguments(context.ActionArguments)) : null,
                ResponseJson = _options.IncludeResponse ? Sanitize(SerializeValue(result?.Data)) : null,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await DispatchAsync(http.RequestServices, record).ConfigureAwait(false);
        }

        /// <summary>
        /// 依次交给所有出口。<b>每个实现独立兜异常</b>：落库炸了不该阻止发 MQ，任何一个炸了都不该影响请求。
        /// <para>不传 <c>RequestAborted</c>：客户端断开就把审计一起取消掉，正是最不该丢日志的时候。</para>
        /// </summary>
        private async ValueTask DispatchAsync(IServiceProvider services, OperationLogRecord record)
        {
            var logger = services.GetService<ILoggerContract>();
            var written = 0;

            try
            {
                foreach (var writer in _writers)
                {
                    written++;
                    try
                    {
                        await writer.WriteAsync(record).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warning("操作日志写入失败（{0}）：{1}", writer.GetType().Name, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // 按需解析的 IEnumerable 在解析实现本身失败时（构造依赖缺失）会在这里抛
                logger?.Warning("操作日志出口解析失败：{0}", ex.Message);
                return;
            }

            if (written == 0)
            {
                logger?.Warning("操作日志未写入：容器里没有任何 IOperationLogWriter，请调用 AddVivOperationLog()。");
            }
        }

        /// <summary>
        /// 分组取控制器名（<c>UserController</c> → <c>user</c>）。<b>这是物理位置，不是业务归类</b>：
        /// 想按业务模块出报表，在消费端配映射表，别回来往框架里加枚举。
        /// </summary>
        private static string? ResolveModule(ActionExecutingContext context)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor descriptor
                && !string.IsNullOrEmpty(descriptor.ControllerName))
            {
                return descriptor.ControllerName.ToLowerInvariant();
            }

            return null;
        }

        /// <summary>
        /// 上下文里 0 表示"没有"，不是"Id 是 0"；落库时空值比 0 更诚实，
        /// 也免得按 UserId 查日志时把匿名请求全捞出来。
        /// </summary>
        private static long? PositiveOrNull(long value) => value > 0 ? value : null;

        /// <summary>
        /// 序列化请求参数。<b>逐个参数序列化再拼</b>，而不是整体一把梭：action 里常带
        /// <c>CancellationToken</c> 这类"读一下就抛"的成员（它的 <c>WaitHandle</c> 就是），
        /// 整体序列化会因为一个参数失败把整份参数丢掉。这里是"坏哪个丢哪个"。
        /// </summary>
        private static string? SerializeArguments(IDictionary<string, object?> arguments)
        {
            if (arguments.Count == 0)
            {
                return null;
            }

            var node = new JsonObject();
            var captured = false;

            foreach (var (name, value) in arguments)
            {
                try
                {
                    node[name] = value is null ? null : JsonSerializer.SerializeToNode(value, JsonOptions);
                    captured = true;
                }
                catch (Exception)
                {
                    // 单个参数序列化不了（Stream / 委托 / getter 抛异常）→ 跳过它，其余照记
                }
            }

            return captured ? node.ToJsonString() : null;
        }

        private static string? SerializeValue(object? value)
        {
            if (value is null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Serialize(value, JsonOptions);
            }
            catch (Exception)
            {
                // 丢一段数据比丢整条记录好：日志本身照记
                return null;
            }
        }

        /// <summary>
        /// 脱敏 + 截断。<b>响应也要脱敏</b>：登录/刷新的响应里就是 AccessToken 与 RefreshToken，
        /// 旧实现只处理了请求，一旦把响应采集补上，那就是把长期凭证原样发进消息队列。
        /// </summary>
        private string? Sanitize(string? payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return payload;
            }

            var masked = SensitiveDataMasker.Mask(payload);
            if (masked is null)
            {
                return null;
            }

            var limit = _options.MaxPayloadChars;
            return limit > 0 && masked.Length > limit
                ? masked[..limit] + "...[截断]"
                : masked;
        }
    }
}
