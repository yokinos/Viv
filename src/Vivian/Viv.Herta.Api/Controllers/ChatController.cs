using Microsoft.AspNetCore.Mvc;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.Entity.Vo.Chat;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        /// <summary>
        /// 发消息
        /// </summary>
        [HttpPost("sendMessage")]
        [ProducesResponseType(typeof(SendMessageOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> SendMessageAsync(SendMessageRequest request)
        {
            return await _chatService.SendMessageAsync(request);
        }

        /// <summary>
        /// 拉取消息
        /// </summary>
        [HttpGet("messages")]
        [ProducesResponseType(typeof(List<MessageItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PullMessagesAsync([FromQuery] PullMessagesRequest request)
        {
            return await _chatService.PullMessagesAsync(request);
        }

        /// <summary>
        /// 会话列表
        /// </summary>
        [HttpGet("sessions")]
        [ProducesResponseType(typeof(List<ChatSessionItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListSessionsAsync([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _chatService.ListSessionsAsync(pageIndex, pageSize);
        }

        /// <summary>
        /// 标已读
        /// </summary>
        [HttpPost("read")]
        public async Task<IActionResult> MarkReadAsync(MarkReadRequest request)
        {
            return await _chatService.MarkReadAsync(request);
        }

        /// <summary>
        /// 建群
        /// </summary>
        [HttpPost("groups")]
        [ProducesResponseType(typeof(CreateGroupOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateGroupAsync(CreateGroupRequest request)
        {
            return await _chatService.CreateGroupAsync(request);
        }

        /// <summary>
        /// 拉人进群
        /// </summary>
        [HttpPost("groups/members/add")]
        public async Task<IActionResult> AddGroupMembersAsync(AddGroupMembersRequest request)
        {
            return await _chatService.AddGroupMembersAsync(request);
        }

        /// <summary>
        /// 退群或踢人
        /// </summary>
        [HttpPost("groups/members/remove")]
        public async Task<IActionResult> RemoveGroupMembersAsync(RemoveGroupMembersRequest request)
        {
            return await _chatService.RemoveGroupMembersAsync(request);
        }

        /// <summary>
        /// 群成员列表
        /// </summary>
        [HttpGet("groups/members")]
        [ProducesResponseType(typeof(List<GroupMemberItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListGroupMembersAsync([FromQuery] long groupId)
        {
            return await _chatService.ListGroupMembersAsync(groupId);
        }

        /// <summary>
        /// 撤回消息
        /// </summary>
        [HttpPost("messages/recall")]
        public async Task<IActionResult> RecallMessageAsync(RecallMessageRequest request)
        {
            return await _chatService.RecallMessageAsync(request);
        }

        /// <summary>
        /// 在线态
        /// </summary>
        [HttpGet("presence")]
        [ProducesResponseType(typeof(List<PresenceItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPresenceAsync([FromQuery] long[] userIds)
        {
            return await _chatService.GetPresenceAsync(userIds);
        }
    }
}
