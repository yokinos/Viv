using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 单聊私聊消息表
    /// </summary>
    public class EtUserChatMessage : EntityBase, ITenant, ISoftDeleted
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 会话Id
        /// </summary>
        public long SessionId { get; set; }

        /// <summary>
        /// 发送人聊天账号ID（关联EtChatUser.Id）
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 接收人聊天账号ID（关联EtChatUser.Id）
        /// </summary>
        public long ReceiverUserId { get; set; }

        /// <summary>
        /// 回复引用的消息Id
        /// </summary>
        public long? ReplyMsgId { get; set; }

        /// <summary>
        /// 消息类型 0文本 1图片 2文件 3语音
        /// </summary>
        public int MsgType { get; set; }

        /// <summary>
        /// 消息文本内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 媒体资源地址，多资源逗号分隔
        /// </summary>
        [StringLength(2000)]
        public string? ResourceUrls { get; set; }

        /// <summary>
        /// 消息投递状态 0待MQ消费 1发送成功 2发送失败
        /// </summary>
        public int SendStatus { get; set; }

        /// <summary>
        /// 接收人是否已读 0未读 1已读
        /// </summary>
        public int IsRead { get; set; }

        /// <summary>
        /// 是否已撤回 0否 1是
        /// </summary>
        public int IsRecall { get; set; }

        /// <summary>
        /// 撤回时间
        /// </summary>
        public DateTime? RecallAt { get; set; }

        /// <summary>
        /// 消息发送时间
        /// </summary>
        public DateTime SendAt { get; set; }

        /// <summary>
        /// 接收人已读时间
        /// </summary>
        public DateTime? ReadAt { get; set; }

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