using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// Optional HTTP/WebSocket transport configuration (headers, proxy).
    /// </summary>
    public sealed class HttpTransportOptions
    {
        public IDictionary<string, string> Headers { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string? ProxyUrl { get; set; }
    }
}
