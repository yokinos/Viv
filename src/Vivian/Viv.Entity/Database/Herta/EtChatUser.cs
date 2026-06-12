using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 所有的会使用聊天的用户都要在这里注册
    /// 设计方式：
    /// 1. 所有需要使用聊天功能的用户都要在这里独立注册，包括系统用户和第三方用户
    /// 2. 共享Apex的App与租户设计
    /// </summary>
    public class EtChatUser : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 用户名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        [StringLength(100)]
        public string? NickName { get; set; }

        /// <summary>
        /// 登录编码（可以是手机号，邮箱，或者其他唯一标识）
        /// 内部系统注册或第三方注册时传入 注意需要第三方自己保存
        /// </summary>
        [StringLength(64)]
        public string? LoginCode { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        [StringLength(32)]
        public string? Password { get; set; }

        /// <summary>
        /// 盐
        /// </summary>
        public string? Salt { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        [StringLength(255)]
        public string? Avatar { get; set; }

        /// <summary>
        /// 聊天角色
        /// </summary>
        public long RoleId { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }
    }
}
