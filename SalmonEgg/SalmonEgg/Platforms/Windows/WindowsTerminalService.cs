using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsTerminalService : ITerminalService
{
    private readonly IReadOnlyList<TerminalDefinition> _definitions;

    public WindowsTerminalService()
    {
        _definitions = DetectTerminals();
    }

    public IReadOnlyList<TerminalDefinition> GetAvailableTerminals() => _definitions;

    public ITerminalSession? CreateSession(TerminalDefinition definition, string? workingDirectory)
    {
        if (definition == null)
        {
            return null;
        }

        if (!File.Exists(definition.ExecutablePath))
        {
            return null;
        }

        var cwd = ResolveWorkingDirectory(workingDirectory);
        return new WindowsTerminalSession(definition, cwd);
    }

    private static string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static IReadOnlyList<TerminalDefinition> DetectTerminals()
    {
        var list = new List<TerminalDefinition>();

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(comSpec) && File.Exists(comSpec))
        {
            list.Add(new TerminalDefinition("cmd", "Command Prompt", comSpec));
        }

        var pwsh = FindOnPath("pwsh.exe");
        if (!string.IsNullOrWhiteSpace(pwsh))
        {
            list.Add(new TerminalDefinition("pwsh", "PowerShell 7", pwsh));
        }

        var windowsPowerShell = FindOnPath("powershell.exe");
        if (!string.IsNullOrWhiteSpace(windowsPowerShell))
        {
            list.Add(new TerminalDefinition("powershell", "Windows PowerShell", windowsPowerShell));
        }

        return list;
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var segment in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(segment.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private sealed class WindowsTerminalSession : ITerminalSession
    {
        private readonly Process _process;
        private bool _isDisposed;

        public WindowsTerminalSession(TerminalDefinition definition, string workingDirectory)
        {
            Id = Guid.NewGuid().ToString("N");
            DisplayName = definition.DisplayName;

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = definition.ExecutablePath,
                    Arguments = definition.Arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<int?>? Exited;

        public void SendInput(string input)
        {
            if (_isDisposed || _process.HasExited)
            {
                return;
            }

            try
            {
                _process.StandardInput.WriteLine(input);
                _process.StandardInput.Flush();
            }
            catch
            {
            }
        }

        public void Kill()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            try
            {
                _process.OutputDataReceived -= OnOutputDataReceived;
                _process.ErrorDataReceived -= OnErrorDataReceived;
                _process.Exited -= OnExited;
            }
            catch
            {
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch
            {
            }

            try
            {
                _process.Dispose();
            }
            catch
            {
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            OutputReceived?.Invoke(this, e.Data + Environment.NewLine);
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            OutputReceived?.Invoke(this, e.Data + Environment.NewLine);
        }

        private void OnExited(object? sender, EventArgs e)
        {
            Exited?.Invoke(this, _process.ExitCode);
        }
    }
}
