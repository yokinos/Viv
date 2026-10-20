namespace Viv.Herta.Core.Entity.Vo.Chat
{
    public class GroupMemberItemOutput
    {
        public long UserId { get; set; }

        /// <summary>0 普通成员 1 管理员 2 群主</summary>
        public int MemberRole { get; set; }

        public string? GroupNickName { get; set; }

        public DateTimeOffset JoinAt { get; set; }
    }
}
