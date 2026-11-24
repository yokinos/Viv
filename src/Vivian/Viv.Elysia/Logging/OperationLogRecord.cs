using System;
using Viv.Entity.Enums;

namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 一条操作日志（进程内模型）。
    ///
    /// <para>为什么不直接用 <c>UserOperationLogEvent</c>：那是<b>跨进程契约</b>，字段只能加不能删
    /// （老消息还要能被新代码读出），且引入它的实现方要额外引用 <c>Viv.EventContracts</c> 与
    /// <c>Viv.Nana</c>。这里是<b>进程内模型</b>，可以随框架一起演进；<c>EventOperationLogWriter</c>
    /// 负责一次性映射过去。两者分开，各自才有独立的兼容包袱。</para>
    ///
    /// <para><b>字段的取舍</b>：审计要回答的是「谁 / 什么时候 / 对哪个对象 / 做了什么 / 结果如何」，
    /// 所以 <see cref="UserId"/>、<see cref="TargetType"/>/<see cref="TargetId"/>、
    /// <see cref="ResultCode"/>/<see cref="Succeeded"/> 是核心；<see cref="Module"/> 只是分组辅助
    /// （自动取控制器名，不是人工归类），<see cref="RequestJson"/>/<see cref="ResponseJson"/> 是排查辅助。</para>
    /// </summary>
    public sealed class OperationLogRecord
    {
        /// <summary>
        /// 功能分组。<b>由过滤器自动填控制器名</b>（<c>UserController</c> → <c>user</c>），
        /// 不是人工声明的业务模块 —— 别在这里手工塞业务词，否则又要把一份词表维护进共享项目。
        /// 需要业务级归类时，在消费端配「控制器 → 业务模块」的映射表，改配置不用发版。
        /// </summary>
        public string? Module { get; init; }

        /// <summary>
        /// 操作类型（新增/修改/删除/审核…），唯一的人工分类。
        /// <para>可空：业务对一个<b>没标</b> <c>[OperationLog]</c> 的接口调了 <c>Target()</c> 时，
        /// 记录意图成立但"是什么操作"确实无从得知 —— 不要编一个 <c>View</c> 出来污染统计。</para>
        /// </summary>
        public EmOperationType? Operation { get; init; }

        /// <summary>动作名，取自 <c>[OperationLog(Name = "...")]</c>，如"新增用户"。</summary>
        public string? Name { get; init; }

        /// <summary>
        /// 日志正文。<see cref="IOperationLogScope.Describe"/> 优先，其次 <see cref="Name"/>，
        /// 最后退回操作类型的中文名。取的是"干了什么"，而不是接口返回给前端的文案。
        /// </summary>
        public string? Description { get; init; }

        /// <summary>操作对象的类型，如 <c>User</c> / <c>Order</c>。与 <see cref="TargetId"/> 配套。</summary>
        public string? TargetType { get; init; }

        /// <summary>操作对象的标识。审计里最值钱的一列——按它才能反查"谁动过这条数据"。</summary>
        public string? TargetId { get; init; }

        /// <summary>操作人（取自请求上下文，不是请求体）。匿名接口（登录/支付回调）为 null。</summary>
        public long? UserId { get; init; }

        /// <summary>主体 Id（租户 / 公司 / 组织），多租户下按它隔离查询。</summary>
        public long? SubjectId { get; init; }

        /// <summary>客户端应用 Id。</summary>
        public long? AppId { get; init; }

        /// <summary>业务信封码（<c>ApiResultCode</c>）。接口未返回 <c>VivApiResult</c> 时按成功记 200。</summary>
        public int ResultCode { get; init; }

        /// <summary>是否成功。失败记录同样有价值（谁试图删了不该删的），默认记录。</summary>
        public bool Succeeded { get; init; }

        /// <summary>action 耗时（毫秒）。异常路径同样统计，因为那正是要排查的。</summary>
        public long DurationMs { get; init; }

        /// <summary>请求追踪 Id，与网关日志、APM 对齐用。</summary>
        public string? TraceId { get; init; }

        /// <summary>请求路径。</summary>
        public string? Path { get; init; }

        /// <summary>客户端 IP。</summary>
        public string? Ip { get; init; }

        /// <summary>客户端 UA。</summary>
        public string? UserAgent { get; init; }

        /// <summary>
        /// 请求参数（<b>已脱敏</b>，可能被截断）。取的是模型绑定后的 <c>ActionArguments</c>，
        /// 不是原始请求流——意味着字段名对得上 DTO，排查时不用再猜。
        /// </summary>
        public string? RequestJson { get; init; }

        /// <summary>响应数据（已脱敏，可能与请求一样被截断）。默认不采集，体积和敏感度都不划算。</summary>
        public string? ResponseJson { get; init; }

        /// <summary>记录时间（UTC）。</summary>
        public DateTimeOffset CreatedAt { get; init; }
    }
}
