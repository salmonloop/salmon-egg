using System;

namespace SalmonEgg.Acp.Client
{
    public enum AcpClientLogLevel
    {
        Trace,
        Information,
        Warning,
        Error
    }

    public interface IAcpClientLogger
    {
        void Log(
            AcpClientLogLevel level,
            string code,
            string message,
            string? source = null,
            Exception? exception = null);
    }

    public sealed class NullAcpClientLogger : IAcpClientLogger
    {
        public void Log(
            AcpClientLogLevel level,
            string code,
            string message,
            string? source = null,
            Exception? exception = null)
        {
        }
    }
}
