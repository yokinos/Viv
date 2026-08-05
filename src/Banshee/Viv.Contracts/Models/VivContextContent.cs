using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Models
{
    /// <summary>
    /// 上下文原始数据模型，纯粹承载字段，无运行时存储逻辑
    /// </summary>
    public class VivContextContent
    {
        /// <summary>
        /// 登录的客户端AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 该App隶属的Id:有可能是TenantId 或者 OrgId 或者 CompanyId
        /// </summary>
        public long SubjectId { get; set; }

        /// <summary>
        /// 登录的用户Id
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 请求Id，用于日志记录和跟踪
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// 克隆一份上下文，用于后台任务传递
        /// </summary>
        public VivContextContent Clone()
        {
            return new VivContextContent
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                RequestId = RequestId
            };
        }

        public bool IsEmpty()
        {
            return AppId == 0 && UserId == 0;
        }

        public override string ToString()
        {
            return $"AppId:{AppId},SubjectId:{SubjectId},UserId:{UserId},RequestId:{RequestId}";
        }
    }
}
