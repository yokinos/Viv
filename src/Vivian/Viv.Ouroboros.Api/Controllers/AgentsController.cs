using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// Agent 管理接口：管理后台用。AgentKey 不可改，档位与提示词版本必须真实存在。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AgentsController : ControllerBase
    {
        private readonly IAgentAdminService _agents;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="agents">Agent 管理服务</param>
        public AgentsController(IAgentAdminService agents)
        {
            _agents = agents;
        }

        /// <summary>
        /// 分页列表（不含已软删的）
        /// </summary>
        /// <param name="agentKey">按业务键过滤</param>
        /// <param name="agentType">按类型过滤，取 EmAgentType</param>
        /// <param name="ownerDomain">按归属域过滤</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>Agent 列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<AgentItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? agentKey, [FromQuery] int? agentType,
            [FromQuery] string? ownerDomain, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _agents.ListAsync(agentKey, agentType, ownerDomain, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情
        /// </summary>
        /// <param name="id">Agent 行 Id</param>
        /// <returns>Agent 详情</returns>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(long id)
        {
            return await _agents.GetAsync(id);
        }

        /// <summary>
        /// 新增
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的 Agent 详情</returns>
        [HttpPost]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateAgentRequest request)
        {
            return await _agents.CreateAsync(request);
        }

        /// <summary>
        /// 修改：AgentKey 不在请求里，改键会让绑定/提示词/会话集体悬空
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的 Agent 详情</returns>
        [HttpPut]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateAgentRequest request)
        {
            return await _agents.UpdateAsync(request);
        }

        /// <summary>
        /// 启用
        /// </summary>
        /// <param name="id">Agent 行 Id</param>
        /// <returns>启用后的 Agent 详情</returns>
        [HttpPost("{id:long}/enable")]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> EnableAsync(long id)
        {
            return await _agents.SetEnabledAsync(id, true, true);
        }

        /// <summary>
        /// 停用：还被启用中的绑定或未结束的会话依赖时，需 force=true 确认
        /// </summary>
        /// <param name="id">Agent 行 Id</param>
        /// <param name="force">是否确认跳过依赖检查</param>
        /// <returns>停用后的 Agent 详情</returns>
        [HttpPost("{id:long}/disable")]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DisableAsync(long id, [FromQuery] bool force = false)
        {
            return await _agents.SetEnabledAsync(id, false, force);
        }

        /// <summary>
        /// 软删：只置 IsDeleted（表有软删列），有依赖时一律拒绝
        /// </summary>
        /// <param name="id">Agent 行 Id</param>
        /// <returns>删除后的 Agent 详情（IsDeleted=true）</returns>
        [HttpPost("{id:long}/delete")]
        [ProducesResponseType(typeof(AgentDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteAsync(long id)
        {
            return await _agents.DeleteAsync(id);
        }
    }
}
