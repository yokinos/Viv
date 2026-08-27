using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天机器人账号表
    /// </summary>
    public class EtChatRobot : EntityBase, ITenant, ISoftDeleted
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 机器人关联聊天账号ID（关联EtChatUser.Id）
        /// </summary>
        public long ChatUserId { get; set; }

        /// <summary>
        /// 机器人名称
        /// </summary>
        [StringLength(100)]
        public string? RobotName { get; set; }

        /// <summary>
        /// 机器人头像
        /// </summary>
        [StringLength(500)]
        public string? Avatar { get; set; }

        /// <summary>
        /// 机器人类型 0自动问答机器人 1通知推送机器人 2欢迎自动回复机器人
        /// </summary>
        public int RobotType { get; set; }

        /// <summary>
        /// 是否启用 0禁用 1启用
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 机器人描述
        /// </summary>
        [StringLength(300)]
        public string? Description { get; set; }

        /// <summary>
        /// 创建人ChatUserId
        /// </summary>
        public long CreateChatUserId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateAt { get; set; }

        /// <summary>
        /// 软删除标记
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}