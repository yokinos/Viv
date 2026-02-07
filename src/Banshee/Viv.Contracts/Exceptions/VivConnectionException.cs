using System;
using System.Runtime.Serialization;
using Viv.Contracts.Enums;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// Viv框架通用连接异常（包装各类资源连接失败场景）
    /// </summary>
    [Serializable]
    public class VivConnectionException : Exception
    {
        /// <summary>
        /// 连接类型
        /// </summary>
        public VivConnType ConnType { get; protected set; }

        /// <summary>
        /// 资源地址
        /// </summary>
        public string ResourceAddress { get; set; } = string.Empty;

        /// <summary>
        /// 基础构造函数（仅指定连接类型+错误消息）
        /// </summary>
        /// <param name="connType">连接类型</param>
        /// <param name="message">错误描述</param>
        public VivConnectionException(VivConnType connType, string message)
            : base(FormatMessage(connType, message, null))
        {
            ConnType = connType;
        }

        /// <summary>
        /// 完整构造函数（包含底层异常，保留堆栈）
        /// </summary>
        /// <param name="connType">连接类型</param>
        /// <param name="message">错误描述</param>
        /// <param name="innerException">底层原始异常</param>
        public VivConnectionException(VivConnType connType, string message, Exception innerException)
            : base(FormatMessage(connType, message, innerException), innerException)
        {
            ConnType = connType;
        }

        /// <summary>
        /// 增强构造函数
        /// </summary>
        /// <param name="connType">连接类型</param>
        /// <param name="resourceAddress">资源地址（如MQ地址、数据库连接串）</param>
        /// <param name="message">错误描述</param>
        /// <param name="innerException">底层原始异常</param>
        public VivConnectionException(VivConnType connType, string resourceAddress, string message, Exception innerException)
            : base(FormatMessage(connType, message, innerException, resourceAddress), innerException)
        {
            ConnType = connType;
            ResourceAddress = resourceAddress;
        }

        /// <summary>
        /// 格式化异常消息（统一格式：[连接类型] 描述 [资源地址] [底层异常]）
        /// </summary>
        private static string FormatMessage(VivConnType connType, string message, Exception? innerException, string resourceAddress = "")
        {
            var msgBuilder = new System.Text.StringBuilder();
            msgBuilder.Append($"[连接类型：{connType}] ");
            msgBuilder.Append(message);
            if (!string.IsNullOrWhiteSpace(resourceAddress))
            {
                msgBuilder.Append($" [资源地址：{resourceAddress}]");
            }
            if (innerException != null)
            {
                msgBuilder.Append($" [底层异常：{innerException.Message}]");
            }
            return msgBuilder.ToString();
        }
    }
}