using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Chat;
using Viv.Entity.Enums;

namespace Viv.Entity.Chat.Message
{
    public class MediaFileMessage : IChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.MediaFile;

        /// <summary>
        /// 文件消息列表
        /// </summary>
        public List<MediaFileInfo> FileList { get; set; }
    }

    public class MediaFileInfo
    {
        public string Url { get; set; } = string.Empty;
        public EmMediaFileType FileType { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileSizeFormatted { get; set; }
    }
}
