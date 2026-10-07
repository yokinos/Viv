using System.Text.Json;

namespace Viv.Ouroboros.Core.Entity.Model.Agent
{
    /// <summary>
    /// 从 MCP 服务发现到的一个工具：名字、描述与入参 schema 都是服务侧给的。
    ///
    /// 只带这三样、不带 SDK 的工具对象，是为了不让"会被缓存 60 秒的装配结果"牵着一条长连接：
    /// 连接归 <see cref="Service.McpClientPool"/> 单独管，调用时按（服务名 + 工具名）现取。
    /// </summary>
    public sealed record McpToolDescriptor(string Name, string Description, JsonElement Schema);
}
