using System;
using Viv.Entity.Enums;

namespace Viv.Elysia.Attributes
{
    /// <summary>
    /// 声明"这个接口是一次需要审计的业务操作"。标上就记，不标不记（opt-in）。
    ///
    /// <para><b>这里只放静态语义</b>：是什么操作（<see cref="Operation"/>）、叫什么名字（<see cref="Name"/>）。
    /// 曾经还有"哪些状态码要记"的参数，已经挪到 <c>OperationLogOptions</c> —— 记成功还是记失败是部署策略，
    /// 不是这个接口的固有属性；写在特性上意味着改策略要改代码、重新发版。</para>
    ///
    /// <para><b>没有"功能模块"参数</b>：日志里的分组由过滤器自动取控制器名（<c>UserController</c> → <c>user</c>）。
    /// 原先那个 <c>EmOperationModule</c> 枚举装在共享项目 <c>Viv.Entity</c> 里、内容却是 Apex 的业务词表，
    /// 每加一个业务模块都要改共享项目并要求所有服务一起重编译；值也已经漂移过（1、2、3、缺 4、5）。
    /// 要业务级归类，在消费端配「控制器 → 业务模块」的映射表，改配置不用发版。</para>
    ///
    /// <para><b>只能标在方法上</b>：模块化之后，"新增"和"修改"是两个不同的操作类型，
    /// 在控制器上统一标注会把整组接口都记成同一种操作，没有意义。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// [OperationLog(EmOperationType.Add, Name = "新增用户")]
    /// public Task&lt;IActionResult&gt; AddAsync(UserAddRequest request) =&gt; _userService.AddAsync(request);
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class OperationLogAttribute : Attribute
    {
        /// <param name="operation">操作类型。描述"做了什么"，取值范围见 <see cref="EmOperationType"/>。</param>
        public OperationLogAttribute(EmOperationType operation)
        {
            if (!Enum.IsDefined(operation))
            {
                throw new ArgumentException(
                    $"[OperationLog] {(int)operation} 不是 EmOperationType 的已定义值。", nameof(operation));
            }

            Operation = operation;
        }

        /// <summary>操作类型（新增/修改/删除/审核/导出/登录…）。</summary>
        public EmOperationType Operation { get; }

        /// <summary>
        /// 动作名，如"新增用户"、"重置密码"。日志正文取它（业务用 <c>Describe</c> 覆盖时以业务为准）。
        ///
        /// <para>这一列<b>不取接口返回的 Message</b>：那是给前端看的提示语，会随文案和多语言漂移，
        /// 失败时还变成错误原因——两类东西混在一列，审计里按内容检索等于没有内容。</para>
        /// </summary>
        public string? Name { get; set; }
    }
}
