using System;
using Viv.Contracts.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo
{
    /// <summary>
    /// Viv框架所有业务实体的基类
    /// </summary>
    public class EntityBase : IEntity
    {
        /// <summary>
        /// 主键ID（自增/雪花ID）
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Viv应用ID（多应用隔离）
        /// </summary>
        public long VivAppId { get; set; }

        /// <summary>
        /// 租户ID（多租户隔离）
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 软删除标识
        /// </summary>
        public VivBool IsDeleted { get; set; } = VivBool.False;

        /// <summary>
        /// 实体创建时间（带时区）
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    }
}