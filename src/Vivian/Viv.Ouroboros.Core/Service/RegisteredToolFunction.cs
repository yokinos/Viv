using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 注册表产出的工具的统一外壳，做三件工厂那层做不到的事：
    /// <list type="number">
    /// <item><description>把工具定义里的 <c>OtTool.ParamsSchema</c> 顶到模型可见的 <c>AIFunction.JsonSchema</c> 上。
    /// 工厂的 schema 是从委托签名推的，而 HTTP 工具的委托只收 <see cref="AIFunctionArguments"/>，推出来是空对象。</description></item>
    /// <item><description>调用前按绑定的 <c>AllowedSubjectIds</c> 校验当前主体：拒绝时**不抛异常**，
    /// 把"无权限"当工具结果交回模型，并留痕成失败 —— 闭包被缓存 60 秒，所以校验必须每次调用现算。</description></item>
    /// <item><description>调用前后落 <c>OtToolCall</c> 留痕（入参、耗时、成功与否、错误信息）。HTTP 工具把执行器
    /// 报的耗时装在 <see cref="HttpToolResult"/> 里带上来，留痕优先用它，模型则只看到里面的正文。</description></item>
    /// </list>
    /// 名字与描述不在这里管：<c>AIFunctionFactory.Create</c> 那一层已经按工具定义显式指定过了。
    /// </summary>
    public sealed class RegisteredToolFunction : DelegatingAIFunction
    {
        private readonly JsonElement? _declaredSchema;
        private readonly string _agentKey;
        private readonly string _toolKey;
        private readonly bool _requiresApproval;
        private readonly string? _allowedSubjectIds;
        private readonly ToolCallRecorder _recorder;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="inner">AIFunctionFactory 造出来的函数</param>
        /// <param name="agentKey">宿主 Agent 业务键（留痕用）</param>
        /// <param name="toolKey">工具键（留痕用）</param>
        /// <param name="requiresApproval">该工具是否需人工审批（留痕用）</param>
        /// <param name="declaredSchema">工具定义里的入参 Schema；没有则沿用工厂推断的那份</param>
        /// <param name="allowedSubjectIds">绑定上的主体白名单原文（空 = 不限制）</param>
        /// <param name="recorder">留痕器</param>
        /// <param name="scopeFactory">作用域工厂：IVivContext 是 Scoped，只能每次调用现开作用域取</param>
        /// <param name="logger">日志</param>
        public RegisteredToolFunction(AIFunction inner, string agentKey, string toolKey, bool requiresApproval,
            JsonElement? declaredSchema, string? allowedSubjectIds, ToolCallRecorder recorder,
            IServiceScopeFactory scopeFactory, ILoggerContract logger) : base(inner)
        {
            _agentKey = agentKey;
            _toolKey = toolKey;
            _requiresApproval = requiresApproval;
            _declaredSchema = declaredSchema;
            _allowedSubjectIds = allowedSubjectIds;
            _recorder = recorder;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 工具定义里写了 ParamsSchema 就以它为准 —— 模型选不选得对工具、参数给得对不对，全看这份 schema。
        /// </summary>
        public override JsonElement JsonSchema => _declaredSchema ?? base.JsonSchema;

        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var serialized = ToolCallRecorder.SerializeArguments(arguments);

            // 白名单判定必须在每次调用时做：闭包被缓存 60 秒，而 subjectId 随请求变
            var subjectId = ResolveSubjectId();
            if (!SubjectAllowList.IsAllowed(_allowedSubjectIds, subjectId, _toolKey, _logger))
            {
                var denied = SubjectAllowList.DenyMessage(subjectId);
                await RecordAsync(serialized, denied, EmToolCallStatus.Failed, denied, Elapsed(startedAt));

                // 不抛异常：无权限是"跑通了但没被执行"，让模型自己把这句话讲给用户
                return denied;
            }

            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken);

                // HTTP 执行器把服务报的耗时随结果带了回来（它比这里掐表准），但模型该看到的是正文：
                // 信封在这一层拆掉，只把 Text 交给模型
                if (result is HttpToolResult http)
                {
                    await RecordAsync(serialized, http.Text, EmToolCallStatus.Succeeded, null, http.LatencyMs);
                    return http.Text;
                }

                await RecordAsync(serialized, result?.ToString(), EmToolCallStatus.Succeeded, null, Elapsed(startedAt));
                return result;
            }
            catch (ToolExecutionException ex)
            {
                // "跑通了但业务失败"（HTTP 非 2xx）：留痕记失败，但把原因当结果交回模型
                await RecordAsync(serialized, ex.Message, EmToolCallStatus.Failed, ex.Reason ?? ex.Message,
                    ex.LatencyMs ?? Elapsed(startedAt));
                return ex.Message;
            }
            catch (Exception ex)
            {
                // 其它异常仍往上抛：MAF 的调用回环会把它变成模型的可见错误，别在这里吞掉
                await RecordAsync(serialized, null, EmToolCallStatus.Failed, ex.Message, Elapsed(startedAt));
                throw;
            }
        }

        /// <summary>
        /// 取当前请求的主体 Id：IVivContext 是 Scoped，只能每次调用现开作用域解析
        /// </summary>
        private long ResolveSubjectId()
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IVivContext>()?.SubjectId ?? 0;
        }

        /// <summary>外壳自己的掐表，只在执行器报不出耗时时兜底</summary>
        private static long Elapsed(long startedAt) => (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        private Task RecordAsync(string? arguments, string? result, EmToolCallStatus status, string? error, long latencyMs)
            => _recorder.WriteAsync(_agentKey, _toolKey, _requiresApproval, arguments, result, status,
                (int)latencyMs, error);
    }
}
