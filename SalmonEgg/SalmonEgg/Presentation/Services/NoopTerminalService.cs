using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Presentation.Services;

public sealed class NoopTerminalService : ITerminalService
{
    private sealed class NoopSession : ITerminalSession
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; } = "Terminal";
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<int?>? Exited;

        public void SendInput(string input)
        {
        }

        public void Kill()
        {
            Exited?.Invoke(this, null);
        }

        public void Dispose()
        {
        }
    }

    public IReadOnlyList<TerminalDefinition> GetAvailableTerminals() => Array.Empty<TerminalDefinition>();

    public ITerminalSession? CreateSession(TerminalDefinition definition, string? workingDirectory) => null;
}
