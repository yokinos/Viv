using Microsoft.AspNetCore.Mvc;
using Viv.Herta.Core.Events;
using Viv.Herta.Core.Models;
using Viv.Nana;

namespace Viv.Herta.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessageController : ControllerBase
    {
        private readonly IVivProducer _producer;

        public MessageController(IVivProducer producer)
        {
            _producer = producer;
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
        {
            var evt = new SendMessageEvent
            {
                MessageId = request.MessageId,
                FromUserId = request.FromUserId,
                ToUserId = request.ToUserId,
                Content = request.Content,
                ContentType = request.ContentType,
                MediaInfo = request.MediaInfo,
                Segments = request.Segments
            };

            var success = await _producer.PublishAsync(evt);

            return success ? Ok(new { MessageId = evt.MessageId }) : StatusCode(500, "Failed to publish message");
        }
    }

    public class SendMessageRequest
    {
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ToUserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public MessageContentType ContentType { get; set; }
        public MediaFileInfo? MediaInfo { get; set; }
        public List<MessageSegment>? Segments { get; set; }
    }
}
