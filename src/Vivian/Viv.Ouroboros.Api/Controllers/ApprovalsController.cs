using Microsoft.AspNetCore.Mvc;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 人工审批接口：写操作在人工批准前不会执行
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ApprovalsController : ControllerBase
    {
        private readonly IAgentChatService _chat;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="chat">对话服务</param>
        public ApprovalsController(IAgentChatService chat)
        {
            _chat = chat;
        }

        /// <summary>
        /// 当前主体的待审批列表
        /// </summary>
        /// <returns>待审批项列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(List<ApprovalItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListPendingAsync()
        {
            return await _chat.ListPendingApprovalsAsync();
        }

        /// <summary>
        /// 批准并继续执行被暂停的那一轮
        /// </summary>
        /// <param name="request">审批请求（内含审批单标识与意见）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>续跑后的助手回复</returns>
        [HttpPost("approve")]
        [ProducesResponseType(typeof(ChatTurnOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> ApproveAsync([FromBody] ApprovalDecisionRequest request, CancellationToken cancellationToken)
        {
            return await _chat.DecideApprovalAsync(true, request, cancellationToken);
        }

        /// <summary>
        /// 拒绝：拒绝结果会作为工具结果回给模型，让它继续对话
        /// </summary>
        /// <param name="request">审批请求（内含审批单标识与拒绝原因）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>续跑后的助手回复</returns>
        [HttpPost("reject")]
        [ProducesResponseType(typeof(ChatTurnOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> RejectAsync([FromBody] ApprovalDecisionRequest request, CancellationToken cancellationToken)
        {
            return await _chat.DecideApprovalAsync(false, request, cancellationToken);
        }
    }
}
