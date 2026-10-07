using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 工具管理接口的新增请求
    /// </summary>
    /// <param name="ToolKey">工具键，也是模型可见的工具名，全局唯一且不可改（能力绑定按它引用）</param>
    /// <param name="OwnerDomain">归属域：apex / herta / deepred / sakumai / ouroboros</param>
    /// <param name="Description">给模型看的描述，写清"什么时候用"</param>
    /// <param name="ParamsSchema">入参 JSON Schema；必须是合法 JSON 对象，否则报错不入库</param>
    /// <param name="Transport">传输方式，取 EmToolTransport</param>
    /// <param name="Endpoint">非进程内时的调用地址</param>
    /// <param name="RequiresApproval">是否需要人工审批，为空按否</param>
    /// <param name="IsReadOnly">是否只读，为空按是</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record CreateToolRequest(string ToolKey, string OwnerDomain, string? Description, string? ParamsSchema,
        int Transport, string? Endpoint, bool? RequiresApproval, bool? IsReadOnly, bool? IsEnabled, string? Remark);

    /// <summary>
    /// 工具管理接口的修改请求：整体覆盖；ToolKey 不在这里，改键会让能力绑定悬空
    /// </summary>
    /// <param name="Id">工具行 Id</param>
    /// <param name="OwnerDomain">归属域</param>
    /// <param name="Description">给模型看的描述</param>
    /// <param name="ParamsSchema">入参 JSON Schema；必须是合法 JSON 对象</param>
    /// <param name="Transport">传输方式，取 EmToolTransport</param>
    /// <param name="Endpoint">调用地址</param>
    /// <param name="RequiresApproval">是否需要人工审批，为空按否</param>
    /// <param name="IsReadOnly">是否只读，为空按是</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record UpdateToolRequest(long Id, string OwnerDomain, string? Description, string? ParamsSchema,
        int Transport, string? Endpoint, bool? RequiresApproval, bool? IsReadOnly, bool? IsEnabled, string? Remark);
}
