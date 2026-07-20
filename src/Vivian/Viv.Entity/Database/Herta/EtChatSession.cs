using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 会话表（私聊会话 / 群聊会话共用）
    /// </summary>
    public class EtChatSession : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 会话类型：0=私聊单聊 1=群聊
        /// </summary>
        public int ChatSessionType { get; set; }

        /// <summary>
        /// 会话所属Id
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 私聊对方用户ID
        /// </summary>
        public long? TargetUserId { get; set; }

        /// <summary>
        /// 群ID 关联 EtGroupChat.Id
        /// </summary>
        public long? GroupChatId { get; set; }

        /// <summary>
        /// 会话置顶
        /// </summary>
        public bool IsTop { get; set; }

        /// <summary>
        /// 消息免打扰
        /// </summary>
        public bool NoDisturb { get; set; }

        /// <summary>
        /// 当前用户最后已读消息ID，计算未读数量
        /// </summary>
        public long LastReadMsgId { get; set; }

        /// <summary>
        /// 会话创建时间
        /// </summary>
        public DateTime CreateAt { get; set; }

        /// <summary>
        /// 最新消息时间，会话列表排序核心字段
        /// </summary>
        public DateTime? LastMessageTime { get; set; }

        /// <summary>
        /// 软删除标记（隐藏会话）
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}