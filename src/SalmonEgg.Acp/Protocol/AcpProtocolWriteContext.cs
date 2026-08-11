using System;
using System.Threading;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// 承载当前序列化调用流的协商协议版本，供无版本的类型 converter
    /// （如 <c>McpServerJsonConverter</c>）在 Write 时按版本分流 wire 形态。
    /// 未显式指定时使用稳定的 <see cref="AcpProtocolVersion.Default"/>；草案 V2
    /// 仅由显式 wire 测试通过 <see cref="Enter"/> 进入。
    /// </summary>
    /// <remarks>
    /// 版本沿同步序列化调用流（<c>JsonSerializer.SerializeToElement</c>）自然传递：
    /// <c>Enter</c> 与序列化之间无 <c>await</c>，同步闭合，并发请求不会串版本。
    /// </remarks>
    internal static class AcpProtocolWriteContext
    {
        private static readonly AsyncLocal<int?> s_protocolVersion = new();

        /// <summary>
        /// 当前调用流的协议版本；未显式进入时默认 <see cref="AcpProtocolVersion.Default"/>。
        /// </summary>
        public static int Current => s_protocolVersion.Value ?? AcpProtocolVersion.Default;

        /// <summary>
        /// 进入指定协议版本的写入上下文，返回的 <see cref="IDisposable"/> 在 Dispose 时恢复上一层版本。
        /// </summary>
        /// <param name="version">协商后的协议版本。</param>
        public static IDisposable Enter(int version)
        {
            var previous = s_protocolVersion.Value;
            s_protocolVersion.Value = version;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly int? _previous;
            private bool _disposed;

            public Scope(int? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                s_protocolVersion.Value = _previous;
            }
        }
    }
}
