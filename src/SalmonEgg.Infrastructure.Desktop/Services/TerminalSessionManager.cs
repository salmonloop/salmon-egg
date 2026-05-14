using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services
{
    /// <summary>
    /// Executes and tracks ACP terminal sessions on behalf of the client.
    /// </summary>
    public sealed class TerminalSessionManager : ITerminalSessionManager
    {
        private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new(StringComparer.Ordinal);

        public Task<TerminalCreateResponse> CreateAsync(TerminalCreateRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Command))
            {
                throw new ArgumentException("Terminal command cannot be empty.", nameof(request));
            }

            var terminalId = Guid.NewGuid().ToString("N");
            var session = TerminalSession.Start(terminalId, request);
            if (!_sessions.TryAdd(terminalId, session))
            {
                session.Dispose();
                throw new InvalidOperationException("Failed to allocate terminal session.");
            }

            return Task.FromResult(new TerminalCreateResponse
            {
                TerminalId = terminalId
            });
        }

        public Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = GetRequiredSession(request?.TerminalId);
            return Task.FromResult(session.CreateOutputResponse());
        }

        public Task<TerminalWaitForExitResponse> WaitForExitAsync(TerminalWaitForExitRequest request, CancellationToken cancellationToken = default)
        {
            var session = GetRequiredSession(request?.TerminalId);
            return session.WaitForExitAsync(cancellationToken);
        }

        public Task<TerminalKillResponse> KillAsync(TerminalKillRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = GetRequiredSession(request?.TerminalId);
            session.Kill();
            return Task.FromResult(new TerminalKillResponse());
        }

        public Task<TerminalReleaseResponse> ReleaseAsync(TerminalReleaseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terminalId = request?.TerminalId;
            if (string.IsNullOrWhiteSpace(terminalId))
            {
                throw new ArgumentException("TerminalId is required.", nameof(request));
            }

            if (_sessions.TryRemove(terminalId, out var session))
            {
                session.Dispose();
            }

            return Task.FromResult(new TerminalReleaseResponse());
        }

        public void Dispose()
        {
            foreach (var entry in _sessions.ToArray())
            {
                if (_sessions.TryRemove(entry.Key, out var session))
                {
                    session.Dispose();
                }
            }
        }

        private TerminalSession GetRequiredSession(string? terminalId)
        {
            if (string.IsNullOrWhiteSpace(terminalId))
            {
                throw new ArgumentException("TerminalId is required.", nameof(terminalId));
            }

            if (_sessions.TryGetValue(terminalId, out var session))
            {
                return session;
            }

            throw new KeyNotFoundException($"Terminal session '{terminalId}' was not found.");
        }

        private sealed class TerminalSession : IDisposable
        {
            private readonly object _gate = new();
            private readonly Process _process;
            private readonly StringBuilder _output = new();
            private readonly TaskCompletionSource<TerminalWaitForExitResponse> _exitCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly int? _outputByteLimit;
            private bool _truncated;
            private bool _disposed;

            private TerminalSession(Process process, int? outputByteLimit)
            {
                _process = process;
                _outputByteLimit = outputByteLimit;
            }

            public static TerminalSession Start(string terminalId, TerminalCreateRequest request)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = request.Command,
                        Arguments = BuildArguments(request.Args),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = string.IsNullOrWhiteSpace(request.Cwd) ? Environment.CurrentDirectory : request.Cwd
                    },
                    EnableRaisingEvents = true
                };

                if (request.Env != null)
                {
                    foreach (var variable in request.Env.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
                    {
                        process.StartInfo.EnvironmentVariables[variable.Name] = variable.Value ?? string.Empty;
                    }
                }

                var session = new TerminalSession(process, request.OutputByteLimit);
                process.OutputDataReceived += (_, args) => session.AppendLine(args.Data);
                process.ErrorDataReceived += (_, args) => session.AppendLine(args.Data);
                process.Exited += (_, _) => session.OnProcessExited();

                if (!process.Start())
                {
                    session.Dispose();
                    throw new InvalidOperationException($"Failed to start terminal command '{request.Command}'.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (process.HasExited)
                {
                    session.OnProcessExited();
                }

                return session;
            }

            public TerminalOutputResponse CreateOutputResponse()
            {
                lock (_gate)
                {
                    return new TerminalOutputResponse
                    {
                        Output = _output.ToString(),
                        Truncated = _truncated,
                        ExitStatus = TryCreateExitStatus()
                    };
                }
            }

            public Task<TerminalWaitForExitResponse> WaitForExitAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_process.HasExited)
                {
                    OnProcessExited();
                }

                if (!cancellationToken.CanBeCanceled)
                {
                    return _exitCompletion.Task;
                }

                return WaitWithCancellationAsync(_exitCompletion.Task, cancellationToken);
            }

            public void Kill()
            {
                if (_process.HasExited)
                {
                    return;
                }

                try
                {
                    _process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit();
                    }
                }
                catch
                {
                }

                _process.Dispose();
            }

            private void AppendLine(string? data)
            {
                if (string.IsNullOrEmpty(data))
                {
                    return;
                }

                lock (_gate)
                {
                    _output.AppendLine(data);
                    TrimOutputIfNeeded();
                }
            }

            private void TrimOutputIfNeeded()
            {
                if (_outputByteLimit == null || _outputByteLimit <= 0)
                {
                    return;
                }

                while (_output.Length > 0 && Encoding.UTF8.GetByteCount(_output.ToString()) > _outputByteLimit.Value)
                {
                    _output.Remove(0, 1);
                    _truncated = true;
                }
            }

            private void OnProcessExited()
            {
                if (_exitCompletion.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    _process.WaitForExit();
                }
                catch
                {
                }

                _exitCompletion.TrySetResult(new TerminalWaitForExitResponse
                {
                    ExitCode = SafeGetExitCode(),
                    Signal = null
                });
            }

            private TerminalExitStatus? TryCreateExitStatus()
            {
                if (!_process.HasExited)
                {
                    return null;
                }

                return new TerminalExitStatus
                {
                    ExitCode = SafeGetExitCode(),
                    Signal = null
                };
            }

            private int? SafeGetExitCode()
            {
                try
                {
                    return _process.ExitCode;
                }
                catch
                {
                    return null;
                }
            }

            private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
            {
                var cancellationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationCompletion))
                {
                    var completed = await Task.WhenAny(task, cancellationCompletion.Task).ConfigureAwait(false);
                    if (completed == task)
                    {
                        return await task.ConfigureAwait(false);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return await task.ConfigureAwait(false);
            }

            private static string BuildArguments(IReadOnlyList<string>? args)
            {
                if (args == null || args.Count == 0)
                {
                    return string.Empty;
                }

                return string.Join(" ", args.Select(EscapeArgument));
            }

            private static string EscapeArgument(string arg)
            {
                if (string.IsNullOrEmpty(arg))
                {
                    return "\"\"";
                }

                if (arg.IndexOfAny(new[] { ' ', '\t', '"', '\\' }) < 0)
                {
                    return arg;
                }

                return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
