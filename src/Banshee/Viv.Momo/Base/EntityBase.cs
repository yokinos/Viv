using System;
using Viv.Contracts.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Base
{
    /// <summary>
    /// 实体基类
    /// </summary>
    [Serializable]
    public class EntityBase : IEntity
    {
        /// <summary>
        /// 主键Id
        /// </summary>
        public long Id { get; set; }
    }
}