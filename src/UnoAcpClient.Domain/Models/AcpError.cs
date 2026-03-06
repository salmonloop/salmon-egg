namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// Represents an error in an ACP response message.
    /// </summary>
    public class AcpError
    {
        /// <summary>
        /// Error code identifying the type of error.
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Additional error data (optional).
        /// Can contain any additional information about the error.
        /// </summary>
        public object? Data { get; set; }
    }
}
