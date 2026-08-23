using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;

namespace Viv.Entity.Any
{
    /// <summary>
    /// 媒体文件
    /// </summary>
    public class MediaFileItem
    {
        /// <summary>
        /// 文件地址
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// 文件大小
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 文件类型
        /// </summary>
        public EmMediaFileType MediaFileType { get; set; }
    }
}
