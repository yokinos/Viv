using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    public enum EmMediaFileType : byte
    {
        [Description("图片")]
        Image = 1,

        [Description("视频")]
        Video = 2,

        [Description("音频")]
        Audio = 3,

        [Description("文档")]
        Document = 4
    }
}
