using System;
using Viv.Contracts.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Base
{
    public class EntityBase : IEntity
    {
        /// <summary>
        /// 主键ID（自增/雪花ID）
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 软删除标识
        /// </summary>
        public VivBool IsDeleted { get; set; } = VivBool.False;
    }
}