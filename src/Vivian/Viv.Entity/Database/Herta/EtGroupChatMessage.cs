using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群聊天消息表
    /// </summary>
    public class EtGroupChatMessage : EntityBase, ITenant, ISoftDeleted
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 群ID，关联 EtGroupChat.Id
        /// </summary>
        public long GroupChatId { get; set; }

        /// <summary>
        /// 会话Id
        /// </summary>
        public long SessionId { get; set; }

        /// <summary>
        /// 发送人用户ID
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 消息类型
        /// 0=文本 1=图片 2=文件 3=语音 4=@指定成员 5=@全体 6=系统通知(入群/退群/改群设置等)
        /// </summary>
        public int MsgType { get; set; }

        /// <summary>
        /// 回复引用的消息Id
        /// </summary>
        public long? ReplyMsgId { get; set; }

        /// <summary>
        /// 消息文本内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 资源地址（图片/文件/语音）多资源逗号分隔
        /// </summary>
        [StringLength(2000)]
        public string? ResourceUrls { get; set; }

        /// <summary>
        /// 被@的用户Id集合，逗号分隔
        /// </summary>
        [StringLength(1000)]
        public string? AtUserIds { get; set; }

        /// <summary>
        /// 是否撤回 0否 1是
        /// </summary>
        public int IsRecall { get; set; }

        /// <summary>
        /// 消息投递状态 0待MQ消费 1发送成功 2发送失败
        /// </summary>
        public int SendStatus { get; set; }

        /// <summary>
        /// 撤回时间
        /// </summary>
        public DateTime? RecallAt { get; set; }

        /// <summary>
        /// 消息发送时间
        /// </summary>
        public DateTime SendAt { get; set; }

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