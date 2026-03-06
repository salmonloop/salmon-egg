namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// 表示连接操作的结果
    /// </summary>
    public class ConnectionResult
    {
        /// <summary>
        /// 指示连接操作是否成功
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 错误消息（如果操作失败）
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionState State { get; }

        private ConnectionResult(bool isSuccess, string error, ConnectionState state)
        {
            IsSuccess = isSuccess;
            Error = error;
            State = state;
        }

        /// <summary>
        /// 创建成功的连接结果
        /// </summary>
        /// <param name="state">连接状态</param>
        /// <returns>成功的连接结果</returns>
        public static ConnectionResult Success(ConnectionState state)
        {
            return new ConnectionResult(true, null, state);
        }

        /// <summary>
        /// 创建失败的连接结果
        /// </summary>
        /// <param name="error">错误消息</param>
        /// <returns>失败的连接结果</returns>
        public static ConnectionResult Failure(string error)
        {
            return new ConnectionResult(false, error, null);
        }
    }
}
