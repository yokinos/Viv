using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }
    }
}