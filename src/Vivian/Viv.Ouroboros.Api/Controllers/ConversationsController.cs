using Microsoft.AspNetCore.Mvc;
using Viv.Contracts.Interface;
using Viv.Elysia.Sse;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 会话与消息接口
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ConversationsController : ControllerBase
    {
        private readonly IConversationService _conversations;
        private readonly IAgentChatService _chat;
        private readonly IVivContext _context;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="conversations">会话服务</param>
        /// <param name="chat">对话服务</param>
        /// <param name="context">请求上下文（取 TraceId 给 SSE 帧用）</param>
        public ConversationsController(IConversationService conversations, IAgentChatService chat, IVivContext context)
        {
            _conversations = conversations;
            _chat = chat;
            _context = context;
        }

        /// <summary>
        /// 创建会话
        /// </summary>
        /// <param name="request">创建会话请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>会话标识</returns>
        [HttpPost]
        [ProducesResponseType(typeof(CreateConversationOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateConversationRequest request, CancellationToken cancellationToken)
        {
            return await _conversations.CreateAsync(request, cancellationToken);
        }

        /// <summary>
        /// 当前主体的会话列表
        /// </summary>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>会话列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(List<ConversationItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _conversations.ListAsync(pageIndex, pageSize);
        }

        /// <summary>
        /// 会话详情（会话头 + 最近若干条消息）
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="lastMessageCount">返回最近多少条消息</param>
        /// <returns>会话详情</returns>
        [HttpGet("{conversationKey:guid}")]
        [ProducesResponseType(typeof(ConversationDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(Guid conversationKey, [FromQuery] int lastMessageCount = 50)
        {
            return await _conversations.GetAsync(conversationKey, lastMessageCount);
        }

        /// <summary>
        /// 增量拉取消息（afterSeq 之后）
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="afterSeq">只取该序号之后的消息</param>
        /// <param name="limit">最多返回条数</param>
        /// <returns>消息列表</returns>
        [HttpGet("{conversationKey:guid}/messages")]
        [ProducesResponseType(typeof(List<MessageItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListMessagesAsync(Guid conversationKey, [FromQuery] int afterSeq = 0, [FromQuery] int limit = 200)
        {
            return await _conversations.ListMessagesAsync(conversationKey, afterSeq, limit);
        }

        /// <summary>
        /// 发一条消息并跑完一轮
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="request">消息内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>助手回复与用量；如需人工审批，返回待审批标识</returns>
        [HttpPost("{conversationKey:guid}/messages")]
        [ProducesResponseType(typeof(ChatTurnOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> SendAsync(Guid conversationKey, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
        {
            return await _chat.SendAsync(conversationKey, request, cancellationToken);
        }

        /// <summary>
        /// 结束会话
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <returns>统一信封</returns>
        [HttpPost("{conversationKey:guid}/close")]
        [ProducesResponseType(typeof(Viv.Engine.VivApiResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> CloseAsync(Guid conversationKey)
        {
            return await _conversations.CloseAsync(conversationKey);
        }

        /// <summary>
        /// 发一条消息并流式返回（SSE）
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="request">消息内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>每帧为 VivSseFrame（code/message/data/traceId），增量帧 data={delta,seq}，末尾为结束帧</returns>
        [HttpPost("{conversationKey:guid}/stream")]
        [ProducesResponseType(typeof(VivSseFrame), StatusCodes.Status200OK)]
        public async Task StreamAsync(Guid conversationKey, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
        {
            await Response.WriteSseStreamAsync(
                _chat.StreamQueuedTurnAsync(conversationKey, request, cancellationToken),
                traceId: _context.TraceId,
                cancellationToken: cancellationToken);
        }
    }
}
