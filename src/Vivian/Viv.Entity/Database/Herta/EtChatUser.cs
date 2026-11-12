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
    /// 3. 按聊天域（<see cref="RealmId"/>）隔离，独立维护聊天昵称、头像、登录凭证、发言权限
    /// <para>
    /// 本表不实现 ITenant：隔离键是 IM 自己的 RealmId，不是 Apex 主体 Id。
    /// 与 Apex 的关联只保留 <see cref="OwnerSubjectId"/> 一处，且仅在登录时用来校验
    /// 「这个主体买了这个 App 没」，不参与本模块的任何查询。
    /// </para>
    /// </summary>
    public class EtChatUser : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— 本模块的隔离键，会话 / 消息 / 成员全部按它分区。
        /// <para>
        /// 由登录注册时分配：同一归属主体默认复用已有的域。要让两个不同主体看到同一批会话
        /// （例如租户的客服对接 Org 的客户），把它们的账号写进同一个 RealmId 即可 ——
        /// 这是本模块表达「跨主体对话」的唯一方式，不需要给会话加「对方主体」之类的列。
        /// </para>
        /// </summary>
        public long RealmId { get; set; }

        /// <summary>
        /// 归属主体 Id，只用于登录时向 Apex 校验应用开通。为空表示这个聊天账号不隶属于任何
        /// Apex 主体（纯 C 端账号、内部机器人），此时跳过 Apex 的授权校验。
        /// <para>
        /// 它<b>不是</b>隔离键：改这一列不会让账号看到别的域的会话，只会改变登录时能否通过授权。
        /// 指向哪一层由 <see cref="UserType"/> 决定 —— OrgUser → AtOrg.Id，
        /// CompanyUser → AtCompany.Id，TenantUser / EndUser → AtTenant.Id。
        /// </para>
        /// </summary>
        public long? OwnerSubjectId { get; set; }

        /// <summary>
        /// 归属主体（<see cref="OwnerSubjectId"/>）的层级，决定登录时查哪一张 Apex 授权表
        /// （见 <c>ApexAppQueryService.ValidateClientAppAsync</c>）。OwnerSubjectId 为空时这一列无意义。
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
        public DateTimeOffset? DeletedAt { get; set; }
        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章，业务代码不写
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章，业务代码不写
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
