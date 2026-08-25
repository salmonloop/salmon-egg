using System;
using System.Threading;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Carries the negotiated protocol version for the current serialization call flow, so that
    /// converters for version-agnostic types (such as <c>McpServerJsonConverter</c>) can branch on the
    /// wire shape while writing. When no version is specified explicitly, the stable
    /// <see cref="AcpProtocolVersion.Default"/> is used; draft V2 is entered only by explicit wire
    /// tests via <see cref="Enter"/>.
    /// </summary>
    /// <remarks>
    /// The version propagates naturally along the synchronous serialization call flow
    /// (<c>JsonSerializer.SerializeToElement</c>): there is no <c>await</c> between <c>Enter</c> and
    /// serialization, so the scope closes synchronously and concurrent requests cannot interleave
    /// versions.
    /// </remarks>
    internal static class AcpProtocolWriteContext
    {
        private static readonly AsyncLocal<int?> s_protocolVersion = new();

        /// <summary>
        /// The protocol version for the current call flow; defaults to
        /// <see cref="AcpProtocolVersion.Default"/> when no version has been entered explicitly.
        /// </summary>
        public static int Current => s_protocolVersion.Value ?? AcpProtocolVersion.Default;

        /// <summary>
        /// Enters a write context for the specified protocol version. Disposing the returned
        /// <see cref="IDisposable"/> restores the version of the enclosing scope.
        /// </summary>
        /// <param name="version">The negotiated protocol version.</param>
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
