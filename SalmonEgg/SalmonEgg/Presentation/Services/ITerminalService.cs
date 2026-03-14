using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Presentation.Services;

public interface ITerminalSession : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    event EventHandler<string> OutputReceived;
    event EventHandler<int?> Exited;
    void SendInput(string input);
    void Kill();
}

public interface ITerminalService
{
    IReadOnlyList<TerminalDefinition> GetAvailableTerminals();
    ITerminalSession? CreateSession(TerminalDefinition definition, string? workingDirectory);
}
