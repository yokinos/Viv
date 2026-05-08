namespace Viv.Herta.Core.Models
{
    public enum MessageContentType
    {
        /// <summary>普通文本</summary>
        Text = 1,

        /// <summary>富文本（HTML / Markdown）</summary>
        RichText = 2,

        /// <summary>媒体文件 URL</summary>
        Media = 3,

        /// <summary>混合消息（文本 + 媒体等组合）</summary>
        Mixed = 4
    }
}
