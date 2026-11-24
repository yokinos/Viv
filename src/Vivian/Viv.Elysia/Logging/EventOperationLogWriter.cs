using System;
using Microsoft.Extensions.DependencyInjection;
using Viv.EventContracts.Apex.Logging;
using Viv.Log;
using Viv.Nana;

namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 默认出口：把记录转成 <see cref="UserOperationLogEvent"/> 发到消息队列。
    ///
    /// <para><b>发布器是可选依赖</b>：只有配了 <c>NanaOption</c> 的宿主才注册 <see cref="IVivEventPublisher"/>。
    /// 早先构造函数里硬要求它，导致"不配 MQ 的服务"每个请求都 500。这里保持惰性取：拿不到就记一条
    /// Warning 收工 —— 日志到不了目的地是配置问题，不该升级成请求故障。</para>
    ///
    /// <para>注册成 Scoped：<see cref="IVivEventPublisher"/> 可能是 Scoped（依赖请求上下文快照），
    /// 从根容器解析会在开启作用域校验的宿主上抛异常。</para>
    /// </summary>
    internal sealed class EventOperationLogWriter : IOperationLogWriter
    {
        private readonly IServiceProvider _services;

        public EventOperationLogWriter(IServiceProvider services)
        {
            _services = services;
        }

        public async ValueTask WriteAsync(OperationLogRecord record, CancellationToken cancellationToken = default)
        {
            var publisher = _services.GetService<IVivEventPublisher>();
            if (publisher is null)
            {
                _services.GetService<ILoggerContract>()?.Warning(
                    "操作日志未发送：未配置消息队列（NanaOption），分组 {0} / 操作 {1}。",
                    record.Module ?? "-", record.Operation?.ToString() ?? "-");
                return;
            }

            await publisher.PublishAsync(ToEvent(record), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 进程内记录 → 跨进程事件。<b>只做字段搬运，不加判断</b>：
        /// 真要在这里做加工，落库和发 MQ 两条路就会得出不同的日志，排查时对不上。
        /// </summary>
        private static UserOperationLogEvent ToEvent(OperationLogRecord record) => new()
        {
            Module = record.Module,
            Operation = record.Operation,
            Name = record.Name,
            Description = record.Description,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            UserId = record.UserId,
            SubjectId = record.SubjectId,
            AppId = record.AppId,
            ResultCode = record.ResultCode,
            Succeeded = record.Succeeded,
            DurationMs = record.DurationMs,
            TraceId = record.TraceId,
            Path = record.Path,
            Ip = record.Ip,
            UserAgent = record.UserAgent,
            RequestJson = record.RequestJson,
            ResponseJson = record.ResponseJson,
            CreatedAt = record.CreatedAt,
        };
    }
}
