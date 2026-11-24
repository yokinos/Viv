using Microsoft.AspNetCore.Http;

namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 一次请求内累积的日志补充信息。过滤器与 <see cref="IOperationLogScope"/> 共用同一个实例。
    ///
    /// <para><b>为什么挂在 <c>HttpContext.Items</c> 而不是 <c>AsyncLocal</c></b>：旧实现用 AsyncLocal
    /// 存一个"可变容器"，因为 AsyncLocal 只从父流向子、子帧里的赋值流不回父帧 —— 那是绕开语言特性，
    /// 换来的是 <c>IsSet</c> 双标志和手工 <c>Clear</c> 两处易错点。而 <c>HttpContext.Items</c> 本来就是
    /// "本次请求共享的一个对象引用"，不存在流向问题，也不会跨请求残留，请求结束随上下文一起回收。</para>
    /// </summary>
    internal sealed class OperationLogBag
    {
        private const string Key = "__viv_operation_log";

        public string? Description { get; set; }

        public string? TargetType { get; set; }

        public string? TargetId { get; set; }

        public bool Skip { get; set; }

        /// <summary>
        /// 业务是否碰过这个包。<c>[OperationLog]</c> 是 opt-in，但业务也可能对一个没标注的接口
        /// 自己调 <c>Target()</c> 表达记录意图；两者任一成立才记录。
        /// <para>用取值反推而不是再加一个 <c>IsSet</c> 标志位：少一个可能忘记置位的状态。</para>
        /// </summary>
        public bool Touched => Skip || Description is not null || TargetId is not null;

        public static OperationLogBag Ensure(HttpContext http)
        {
            if (http.Items[Key] is OperationLogBag existing)
            {
                return existing;
            }

            var bag = new OperationLogBag();
            http.Items[Key] = bag;
            return bag;
        }

        public static OperationLogBag? Get(HttpContext? http) => http?.Items[Key] as OperationLogBag;
    }
}
