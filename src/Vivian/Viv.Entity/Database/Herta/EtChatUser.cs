using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天模块用户表
    /// 设计说明：
    /// 1. 所有需要使用聊天功能的主体在此注册（业务用户、机器人、管理员）
    /// 2. 聊天模块独立ID体系，外部业务自行存储 ChatUserId 关联本表，本表不存储外部业务ID
    /// 3. 租户隔离，独立维护聊天昵称、头像、登录凭证、发言权限
    /// </summary>
    public class EtChatUser : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 聊天展示昵称
        /// </summary>
        [StringLength(100)]
        public string? NickName { get; set; }

        /// <summary>
        /// 个性签名
        /// </summary>
        [StringLength(200)]
        public string? Signature { get; set; }

        /// <summary>
        /// 登录唯一标识
        /// </summary>
        [StringLength(64)]
        public string? LoginCode { get; set; }

        /// <summary>
        /// 密码哈希
        /// </summary>
        [StringLength(64)]
        public string? Password { get; set; }

        /// <summary>
        /// 密码盐值
        /// </summary>
        [StringLength(32)]
        public string? Salt { get; set; }

        /// <summary>
        /// 头像地址
        /// </summary>
        [StringLength(255)]
        public string? Avatar { get; set; }

        /// <summary>
        /// 聊天身份角色 0普通用户 1客服 2机器人 3超管
        /// </summary>
        public int ChatRole { get; set; }

        /// <summary>
        /// 是否永久禁言
        /// </summary>
        public bool IsMute { get; set; }

        /// <summary>
        /// 禁言到期时间，null代表永久禁言
        /// </summary>
        public DateTime? MuteEndAt { get; set; }

        /// <summary>
        /// 上次上线时间
        /// </summary>
        public DateTime? LastOnlineAt { get; set; }

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