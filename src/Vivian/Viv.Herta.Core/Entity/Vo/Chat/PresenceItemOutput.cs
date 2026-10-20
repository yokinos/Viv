namespace Viv.Herta.Core.Entity.Vo.Chat
{
    public class PresenceItemOutput
    {
        public long UserId { get; set; }

        /// <summary>null = 查询失败，状态未知</summary>
        public bool? Online { get; set; }
    }
}
