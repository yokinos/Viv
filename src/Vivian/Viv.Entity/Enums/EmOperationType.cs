using System.ComponentModel;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 操作类型
    /// </summary>
    public enum EmOperationType
    {
        /// <summary>
        /// 新增
        /// </summary>
        [Description("新增")]
        Add = 1,

        /// <summary>
        /// 修改
        /// </summary>
        [Description("修改")]
        Edit = 2,

        /// <summary>
        /// 删除
        /// </summary>
        [Description("删除")]
        Delete = 3,

        /// <summary>
        /// 审核通过
        /// </summary>
        [Description("审核通过")]
        AuditApprove = 4,

        /// <summary>
        /// 审核驳回
        /// </summary>
        [Description("审核驳回")]
        AuditReject = 5,

        /// <summary>
        /// 导出
        /// </summary>
        [Description("导出")]
        Export = 6,

        /// <summary>
        /// 登录
        /// </summary>
        [Description("登录")]
        Login = 7,

        /// <summary>
        /// 退出登录
        /// </summary>
        [Description("退出登录")]
        Logout = 8,

        /// <summary>
        /// 查看
        /// </summary>
        [Description("查看")]
        View = 9
    }
}