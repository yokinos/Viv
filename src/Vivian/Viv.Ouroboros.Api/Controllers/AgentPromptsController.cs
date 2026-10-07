using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 提示词管理接口：管理后台用。新增只落版本，生效版本由 activate 显式切换。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AgentPromptsController : ControllerBase
    {
        private readonly IAgentPromptAdminService _prompts;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="prompts">提示词管理服务</param>
        public AgentPromptsController(IAgentPromptAdminService prompts)
        {
            _prompts = prompts;
        }

        /// <summary>
        /// 分页列表（不回正文）
        /// </summary>
        /// <param name="agentKey">按 Agent 业务键过滤，不给则全部</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>提示词版本列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<AgentPromptItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? agentKey,
            [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _prompts.ListAsync(agentKey, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情：按 AgentKey + Version 取
        /// </summary>
        /// <param name="agentKey">Agent 业务键</param>
        /// <param name="version">版本号</param>
        /// <returns>提示词详情</returns>
        [HttpGet("{agentKey}/{version:int}")]
        [ProducesResponseType(typeof(AgentPromptDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(string agentKey, int version)
        {
            return await _prompts.GetAsync(agentKey, version);
        }

        /// <summary>
        /// 新增一个版本（version 不给则取当前最大版本 + 1），不自动生效
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的提示词详情</returns>
        [HttpPost]
        [ProducesResponseType(typeof(AgentPromptDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateAgentPromptRequest request)
        {
            return await _prompts.CreateAsync(request);
        }

        /// <summary>
        /// 修改正文：当前生效的那一版不许改
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的提示词详情</returns>
        [HttpPut]
        [ProducesResponseType(typeof(AgentPromptDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateAgentPromptRequest request)
        {
            return await _prompts.UpdateAsync(request);
        }

        /// <summary>
        /// 切换 Agent 生效的提示词版本（写操作完成后配置立即生效，无需手动 refresh）
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <returns>切换后生效的提示词详情</returns>
        [HttpPost("activate")]
        [ProducesResponseType(typeof(AgentPromptDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> ActivateAsync([FromBody] ActivateAgentPromptRequest request)
        {
            return await _prompts.ActivateAsync(request);
        }

        /// <summary>
        /// 删除某个版本：当前生效的那一版不许删
        /// </summary>
        /// <param name="id">提示词行 Id</param>
        /// <returns>统一信封</returns>
        [HttpDelete("{id:long}")]
        [ProducesResponseType(typeof(VivApiResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteAsync(long id)
        {
            return await _prompts.DeleteAsync(id);
        }
    }
}
