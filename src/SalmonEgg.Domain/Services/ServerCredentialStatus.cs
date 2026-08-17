namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 服务器凭据的存在性状态。
    /// </summary>
    /// <remarks>
    /// 只承载存在性，不承载凭据值：调用方（例如 CLI 的 has-credential）需要判断是否已登记，
    /// 但不允许回显明文。把值留在类型之外，使"不回显"成为类型契约而非调用方自觉。
    /// </remarks>
    /// <param name="HasToken">是否已登记 bearer token</param>
    /// <param name="HasApiKey">是否已登记 API key</param>
    public readonly record struct ServerCredentialStatus(bool HasToken, bool HasApiKey)
    {
        /// <summary>
        /// 该服务器是否登记了任意一种凭据。
        /// </summary>
        public bool HasAny => HasToken || HasApiKey;
    }
}
