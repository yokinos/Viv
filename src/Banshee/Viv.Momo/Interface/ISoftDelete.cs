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
        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }
    }
}
