using System;
using Viv.Entity.Enums;
using Viv.Nana;

namespace Viv.EventContracts.Apex.Logging
{
    /// <summary>
    /// 用户操作日志事件（跨进程契约）。由 <c>EventOperationLogWriter</c> 发布，
    /// 通常由 Worker 消费后落库。
    ///
    /// <para><b>兼容性纪律</b>：这个类型会作为消息体序列化，生产环境里可能存在旧版本消息。
    /// 只加字段、不删不改语义；新增字段一律可空或带默认值。</para>
    /// </summary>
    public class UserOperationLogEvent : NanaEvent
    {
        public UserOperationLogEvent() { }

        /// <summary>
        /// 功能分组。<b>自动取控制器名</b>（<c>UserController</c> → <c>user</c>），
        /// 不是人工声明的业务模块。要业务级归类，在消费端做映射表。
        /// </summary>
        public string? Module { get; set; }

        /// <summary>
        /// 操作类型。可空：业务对未标注 <c>[OperationLog]</c> 的接口主动记日志时，类型确实未知
        /// （旧版本消息反序列化到这里也是 null）。
        /// </summary>
        public EmOperationType? Operation { get; set; }

        /// <summary>
        /// 动作名，如"新增用户"。与 <see cref="Description"/> 分开：
        /// 前者是固定的动作标识，后者是本次执行的具体描述（"把张三的角色从 A 调整为 B"）。
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 日志正文。取值顺序：业务 Describe → 动作名 → 操作类型中文名。
        /// <b>不再是接口返回给前端的 Message</b>。
        /// </summary>
        public string? Description { get; set; }

        /// <summary>操作对象类型，如 <c>User</c> / <c>Order</c>。</summary>
        public string? TargetType { get; set; }

        /// <summary>操作对象标识 —— 审计里最值钱的一列，按它反查"谁动过这条数据"。</summary>
        public string? TargetId { get; set; }

        /// <summary>操作人 Id。匿名接口（登录、支付回调）为 null。</summary>
        public long? UserId { get; set; }

        /// <summary>主体 Id（租户 / 公司 / 组织）。</summary>
        public long? SubjectId { get; set; }

        /// <summary>客户端应用 Id。</summary>
        public long? AppId { get; set; }

        /// <summary>业务信封码（<c>ApiResultCode</c>）。</summary>
        public int ResultCode { get; set; }

        /// <summary>是否成功。失败记录同样入库 —— 审计最需要留痕的是失败尝试。</summary>
        public bool Succeeded { get; set; }

        /// <summary>action 耗时（毫秒）。</summary>
        public long DurationMs { get; set; }

        /// <summary>请求追踪 Id，与网关日志 / APM 对齐用。</summary>
        public string? TraceId { get; set; }

        /// <summary>请求路径。</summary>
        public string? Path { get; set; }

        /// <summary>客户端 IP。</summary>
        public string? Ip { get; set; }

        /// <summary>客户端 UA。</summary>
        public string? UserAgent { get; set; }

        /// <summary>请求参数（已脱敏，可能被截断）。</summary>
        public string? RequestJson { get; set; }

        /// <summary>响应数据（已脱敏，可能被截断）。默认不采集。</summary>
        public string? ResponseJson { get; set; }

        /// <summary>记录时间（UTC）。由采集侧生成，不依赖消费时间。</summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}
