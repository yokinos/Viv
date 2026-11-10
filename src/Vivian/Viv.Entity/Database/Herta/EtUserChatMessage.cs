using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 单聊私聊消息表
    /// </summary>
    public class EtUserChatMessage : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— IM 自己的隔离键，与 Apex 主体无关，由本模块分配。
        /// 本实体刻意不实现 ITenant：Momo 的租户盖章与全局过滤器不再作用于它，
        /// 隔离一律靠查询里的显式条件，漏写一处就是跨域可见，改查询时务必带上。
        /// </summary>
        public long RealmId { get; set; }

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
