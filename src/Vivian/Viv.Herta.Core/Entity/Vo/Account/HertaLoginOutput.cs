namespace Viv.Herta.Core.Entity.Vo.Account
{
    public class HertaLoginOutput
    {
        /// <summary>IM 自己签发的 token，后续请求带它即可，不依赖平台身份</summary>
        public string Token { get; set; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; set; }

        public long ChatUserId { get; set; }

        public string? NickName { get; set; }

        public string? Avatar { get; set; }
    }
}
