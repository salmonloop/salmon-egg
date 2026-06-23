namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// 代理配置
    /// </summary>
    public class ProxyConfig
    {
        public const ProxyMode DefaultMode = ProxyMode.System;

        /// <summary>
        /// 代理模式
        /// </summary>
        public ProxyMode Mode { get; set; } = DefaultMode;

        /// <summary>
        /// 代理服务器 URL
        /// </summary>
        public string? ProxyUrl { get; set; }
    }
}
