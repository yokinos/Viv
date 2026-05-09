using Microsoft.AspNetCore.Mvc;
using Viv.Herta.Core.Entity.ViewModel.Chat;
using Viv.Herta.Core.IService;
using Viv.Nana;

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

        [HttpPost("sendMessage")]
        public async Task<IActionResult> SendMessageAsync(SendMessageRequest request)
        {
            return await _chatService.SendMessageAsync(request);
        }
    }
}
