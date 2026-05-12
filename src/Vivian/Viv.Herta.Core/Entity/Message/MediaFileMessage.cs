using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Entity.Interface;

namespace Viv.Herta.Core.Entity.Message
{
    public class MediaFileMessage : IChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.MediaFile;

        /// <summary>
        /// 文件消息列表
        /// </summary>
        public List<MediaFileInfo> FileList { get; set; }

        /// <summary>
        /// 扩展消息
        /// </summary>
        public Dictionary<string, object> Extend { get; set; } = [];
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
