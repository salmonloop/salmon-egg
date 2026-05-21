using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.Mcp
{
    /// <summary>
    /// MCP 服务目录设置。
    /// </summary>
    public sealed class McpSettings
    {
        /// <summary>
        /// MCP server catalog。
        /// </summary>
        public List<McpServer> Servers { get; set; } = new List<McpServer>();
    }
}
