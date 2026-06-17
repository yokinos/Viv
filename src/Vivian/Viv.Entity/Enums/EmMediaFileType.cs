using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Entity.Enums
{
    public enum EmMediaFileType : byte
    {
        [EnumName("图片")]
        Image = 1,

        [EnumName("视频")]
        Video = 2,

        [EnumName("音频")]
        Audio = 3,

        [EnumName("文档")]
        Document = 4
    }
}
