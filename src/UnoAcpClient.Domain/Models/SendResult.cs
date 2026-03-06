namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// 表示消息发送操作的结果
    /// </summary>
    public class SendResult
    {
        /// <summary>
        /// 指示发送操作是否成功
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 错误消息（如果操作失败）
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// 发送的消息 ID
        /// </summary>
        public string MessageId { get; }

        private SendResult(bool isSuccess, string error, string messageId)
        {
            IsSuccess = isSuccess;
            Error = error;
            MessageId = messageId;
        }

        /// <summary>
        /// 创建成功的发送结果
        /// </summary>
        /// <param name="messageId">发送的消息 ID</param>
        /// <returns>成功的发送结果</returns>
        public static SendResult Success(string messageId)
        {
            return new SendResult(true, null, messageId);
        }

        /// <summary>
        /// 创建失败的发送结果
        /// </summary>
        /// <param name="error">错误消息</param>
        /// <returns>失败的发送结果</returns>
        public static SendResult Failure(string error)
        {
            return new SendResult(false, error, null);
        }
    }
}
