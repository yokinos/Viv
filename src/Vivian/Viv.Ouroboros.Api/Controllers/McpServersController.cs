using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// MCP 服务管理接口：管理后台用。写操作会抬配置版本戳，工具装配与 MCP 连接随之失效。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class McpServersController : ControllerBase
    {
        private readonly IMcpServerAdminService _servers;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="servers">MCP 服务管理服务</param>
        public McpServersController(IMcpServerAdminService servers)
        {
            _servers = servers;
        }

        /// <summary>
        /// 分页列表（不回请求头内容）
        /// </summary>
        /// <param name="serverName">按服务名过滤</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>MCP 服务列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<McpServerItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? serverName,
            [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _servers.ListAsync(serverName, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情（不回请求头内容）
        /// </summary>
        /// <param name="id">MCP 服务行 Id</param>
        /// <returns>MCP 服务详情</returns>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(McpServerDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(long id)
        {
            return await _servers.GetAsync(id);
        }

        /// <summary>
        /// 新增
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的 MCP 服务详情</returns>
        [HttpPost]
        [ProducesResponseType(typeof(McpServerDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateMcpServerRequest request)
        {
            return await _servers.CreateAsync(request);
        }

        /// <summary>
        /// 修改：headers 为 null 表示不改动现有请求头
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的 MCP 服务详情</returns>
        [HttpPut]
        [ProducesResponseType(typeof(McpServerDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateMcpServerRequest request)
        {
            return await _servers.UpdateAsync(request);
        }

        /// <summary>
        /// 启用
        /// </summary>
        /// <param name="id">MCP 服务行 Id</param>
        /// <returns>启用后的 MCP 服务详情</returns>
        [HttpPost("{id:long}/enable")]
        [ProducesResponseType(typeof(McpServerDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> EnableAsync(long id)
        {
            return await _servers.SetEnabledAsync(id, true, true);
        }

        /// <summary>
        /// 停用：还被启用中的绑定引用时，需 force=true 确认
        /// </summary>
        /// <param name="id">MCP 服务行 Id</param>
        /// <param name="force">是否确认跳过依赖检查</param>
        /// <returns>停用后的 MCP 服务详情</returns>
        [HttpPost("{id:long}/disable")]
        [ProducesResponseType(typeof(McpServerDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DisableAsync(long id, [FromQuery] bool force = false)
        {
            return await _servers.SetEnabledAsync(id, false, force);
        }
    }
}
