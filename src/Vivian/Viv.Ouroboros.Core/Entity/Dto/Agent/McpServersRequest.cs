using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// MCP 服务管理接口的新增请求
    /// </summary>
    /// <param name="ServerName">服务名，全局唯一且不可改（能力绑定按它引用）</param>
    /// <param name="Transport">传输方式，取 EmMcpTransport</param>
    /// <param name="ServerAddress">服务地址（HTTP/SSE 时）</param>
    /// <param name="ServerDescription">服务描述，会作为挑选依据展示给模型</param>
    /// <param name="AllowedTools">允许暴露的工具名 JSON 数组，空白 = 全部</param>
    /// <param name="Headers">调用时附加的请求头 JSON 对象（可能含内部令牌，读接口不回显，只回 hasHeaders）</param>
    /// <param name="ApprovalMode">审批模式，取 EmMcpApprovalMode</param>
    /// <param name="AlwaysRequireToolNames">ByList 模式下必须审批的工具名 JSON 数组</param>
    /// <param name="NeverRequireToolNames">ByList 模式下免审批的工具名 JSON 数组</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    public sealed record CreateMcpServerRequest(string ServerName, int Transport, string? ServerAddress, string? ServerDescription,
        string? AllowedTools, string? Headers, int? ApprovalMode, string? AlwaysRequireToolNames, string? NeverRequireToolNames, bool? IsEnabled);

    /// <summary>
    /// MCP 服务管理接口的修改请求：整体覆盖；ServerName 不在这里，改服务名会让能力绑定悬空
    /// </summary>
    /// <param name="Id">MCP 服务行 Id</param>
    /// <param name="Transport">传输方式，取 EmMcpTransport</param>
    /// <param name="ServerAddress">服务地址</param>
    /// <param name="ServerDescription">服务描述</param>
    /// <param name="AllowedTools">允许暴露的工具名 JSON 数组</param>
    /// <param name="Headers">请求头 JSON 对象；**null = 不改动现有请求头，空串 = 清空，非空 = 整体替换**</param>
    /// <param name="ApprovalMode">审批模式，取 EmMcpApprovalMode</param>
    /// <param name="AlwaysRequireToolNames">必须审批的工具名 JSON 数组</param>
    /// <param name="NeverRequireToolNames">免审批的工具名 JSON 数组</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    public sealed record UpdateMcpServerRequest(long Id, int Transport, string? ServerAddress, string? ServerDescription,
        string? AllowedTools, string? Headers, int? ApprovalMode, string? AlwaysRequireToolNames, string? NeverRequireToolNames, bool? IsEnabled);
}
