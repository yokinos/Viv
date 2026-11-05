using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
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
    public class EtChatUser : EntityBase, ITenant, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 归属主体 Id —— 由 <see cref="UserType"/> 决定它指向哪一层：
        /// OrgUser → AtOrg.Id，CompanyUser → AtCompany.Id，TenantUser / EndUser → AtTenant.Id。
        /// <para>
        /// Master 也要求填一个真实租户 Id：这个实体实现 ITenant，租户过滤器靠这一列做隔离，
        /// 给 0 会退化成「什么都查不到」（EF）或「什么都看得见」（原生 SQL），两种都不安全。
        /// 所以 Master 的「至高无上」体现在<b>不校验 App 授权</b>，而不是取消租户上下文。
        /// </para>
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 用户类型，决定登录时查哪一层的主体授权表（见 <c>ApexAppQueryService.ValidateClientAppAsync</c>）。
        /// Master 不查授权表，只校验用户存在；注册接口一律落 EndUser，不允许客户端指定。
        /// <para>
        /// 初始值取 EndUser 而不是枚举默认的 0：0 是 Master，代码里 new 出来忘了赋值就会变成
        /// 「跳过全部授权校验」的账号。DB 侧新增列也必须显式写 DEFAULT 10，否则存量行会全变 Master。
        /// </para>
        /// </summary>
        public EmUserType UserType { get; set; } = EmUserType.EndUser;

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
        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章，业务代码不写
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章，业务代码不写
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
