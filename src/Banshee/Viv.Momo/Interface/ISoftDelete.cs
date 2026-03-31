using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Interface
{
    /// <summary>
    /// 软删除定义
    /// </summary>
    public interface ISoftDelete
    {
         bool IsDeleted { get; set; }

         DateTime? DeletedAt { get; set; }
    }
}
