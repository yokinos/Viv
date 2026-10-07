using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 自检接口：档位解析、Agent 装配、真实对话、清缓存；正式业务接口上来后可删
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AgentDiagnosticsController : ControllerBase
    {
        private readonly IAgentDiagnosticsService _diagnostics;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="diagnostics">自检服务</param>
        public AgentDiagnosticsController(IAgentDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// 查看某个档位解析结果
        /// </summary>
        /// <param name="profileKey">档位键</param>
        /// <returns>档位信息（不含密钥明文）</returns>
        [HttpGet("profile/{profileKey}")]
        [ProducesResponseType(typeof(ModelProfileOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProfileAsync(string profileKey)
        {
            return await _diagnostics.GetProfileAsync(profileKey);
        }

        /// <summary>
        /// 查看某个 Agent 能否装配出来
        /// </summary>
        /// <param name="agentKey">Agent 业务键</param>
        /// <returns>Agent 基本信息</returns>
        [HttpGet("agent/{agentKey}")]
        [ProducesResponseType(typeof(AgentInfoOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAgentAsync(string agentKey)
        {
            return await _diagnostics.GetAgentAsync(agentKey);
        }

        /// <summary>
        /// 真跑一次对话（会消耗 token）
        /// </summary>
        /// <param name="agentKey">Agent 业务键</param>
        /// <param name="text">用户输入</param>
        /// <returns>回复正文与 token 用量</returns>
        [HttpGet("chat")]
        [ProducesResponseType(typeof(ChatTurnOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChatAsync([FromQuery] string agentKey, [FromQuery] string text)
        {
            return await _diagnostics.ChatAsync(agentKey, text);
        }

        /// <summary>
        /// 投递一轮到队列（只落用户消息 + 发事件，跑模型的是 Worker）
        /// </summary>
        /// <param name="request">会话标识与用户输入</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>用户消息 Id</returns>
        [HttpPost("queueTurn")]
        [ProducesResponseType(typeof(QueueTurnOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> QueueTurnAsync([FromBody] QueueTurnRequest request, CancellationToken cancellationToken)
        {
            // 身份由 VivContextMiddleware 按 [Authorize] 水合进 IVivContext（再没有匿名逃生的那条旁路），
            // Service 侧直接读 _context 做越权校验即可
            return await _diagnostics.QueueTurnAsync(request, cancellationToken);
        }

        /// <summary>
        /// 清缓存：改完库里的档位或 Agent 定义后调用
        /// </summary>
        /// <param name="agentKey">要清缓存的 Agent 业务键</param>
        /// <param name="profileKey">要清缓存的档位键</param>
        /// <returns>统一信封</returns>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(Viv.Engine.VivApiResult), StatusCodes.Status200OK)]
        public IActionResult Refresh([FromQuery] string? agentKey, [FromQuery] string? profileKey)
        {
            return _diagnostics.Refresh(agentKey, profileKey);
        }
    }
}
