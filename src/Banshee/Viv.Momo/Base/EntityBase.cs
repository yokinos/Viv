using System;
using Viv.Contracts.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Base
{
    public class EntityBase : IEntity
    {
        /// <summary>
        /// 主键Id
        /// </summary>
        public long Id { get; set; }
    }
}