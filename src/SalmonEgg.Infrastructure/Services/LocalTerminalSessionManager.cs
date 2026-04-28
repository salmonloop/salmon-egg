using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

/// <summary>
/// Manages conversation-scoped local interactive shell sessions.
/// </summary>
public sealed class LocalTerminalSessionManager : ILocalTerminalSessionManager
{
    private const int DefaultInitialColumns = 120;
    private const int DefaultInitialRows = 32;
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Func<string, string, CancellationToken, ValueTask<ILocalTerminalSession>> _sessionFactory;
    private int _disposed;

    public LocalTerminalSessionManager()
        : this(CreatePtyBackedSessionAsync)
    {
    }

    internal LocalTerminalSessionManager(
        Func<string, string, CancellationToken, ValueTask<ILocalTerminalSession>> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async ValueTask<ILocalTerminalSession> GetOrCreateAsync(
        string conversationId,
        string preferredCwd,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("ConversationId is required.", nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(preferredCwd))
        {
            throw new ArgumentException("Preferred current working directory is required.", nameof(preferredCwd));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var entry = _sessions.GetOrAdd(
                conversationId,
                _ => new SessionEntry(conversationId, _sessionFactory));

            return await entry.GetOrCreateAsync(preferredCwd, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("ConversationId is required.", nameof(conversationId));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_sessions.TryRemove(conversationId, out var entry))
            {
                await entry.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var pair in _sessions.ToArray())
            {
                if (_sessions.TryRemove(pair.Key, out var entry))
                {
                    await entry.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LocalTerminalSessionManager));
        }
    }

    private static async ValueTask<ILocalTerminalSession> CreatePtyBackedSessionAsync(
        string conversationId,
        string preferredCwd,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var launchInfo = ResolveShellLaunchInfo();
        var options = new PtyOptions
        {
            Name = $"salmonegg-local-terminal-{conversationId}",
            Cols = DefaultInitialColumns,
            Rows = DefaultInitialRows,
            Cwd = preferredCwd,
            App = launchInfo.App,
            CommandLine = launchInfo.Arguments,
            Environment = CaptureEnvironmentVariables()
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken).ConfigureAwait(false);
        var session = new PtyBackedLocalTerminalSession(conversationId, preferredCwd, connection);
        session.AttachToRunningConnection();
        return session;
    }

    private static Dictionary<string, string> CaptureEnvironmentVariables()
    {
        var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(comparer);
        foreach (var keyObject in Environment.GetEnvironmentVariables().Keys)
        {
            if (keyObject is not string key || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                environment[key] = value;
            }
        }

        return environment;
    }

    private static ShellLaunchInfo ResolveShellLaunchInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var comSpec = Environment.GetEnvironmentVariable("COMSPEC");
            return new ShellLaunchInfo(
                string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec,
                new[] { "/Q" });
        }

        var shellPath = Environment.GetEnvironmentVariable("SHELL");
        return new ShellLaunchInfo(
            string.IsNullOrWhiteSpace(shellPath) ? "/bin/sh" : shellPath,
            new[] { "-i" });
    }

    private sealed class ShellLaunchInfo
    {
        public ShellLaunchInfo(string app, string[] arguments)
        {
            App = app;
            Arguments = arguments;
        }

        public string App { get; }

        public string[] Arguments { get; }
    }

    private sealed class SessionEntry
    {
        private readonly string _conversationId;
        private readonly Func<string, string, CancellationToken, ValueTask<ILocalTerminalSession>> _sessionFactory;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ILocalTerminalSession? _session;
        private bool _disposed;

        public SessionEntry(
            string conversationId,
            Func<string, string, CancellationToken, ValueTask<ILocalTerminalSession>> sessionFactory)
        {
            _conversationId = conversationId;
            _sessionFactory = sessionFactory;
        }

        public async ValueTask<ILocalTerminalSession> GetOrCreateAsync(
            string preferredCwd,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                if (_session != null)
                {
                    if (_session.CanAcceptInput)
                    {
                        return _session;
                    }

                    var deadSession = _session;
                    _session = null;
                    await deadSession.DisposeAsync().ConfigureAwait(false);
                }

                var session = await _sessionFactory(_conversationId, preferredCwd, cancellationToken).ConfigureAwait(false);
                if (session == null)
                {
                    throw new InvalidOperationException("Local terminal session factory returned null.");
                }

                if (!session.CanAcceptInput)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("Local terminal session is not accepting input.");
                }

                if (_disposed)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw new ObjectDisposedException(nameof(LocalTerminalSessionManager));
                }

                _session = session;
                return session;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            ILocalTerminalSession? sessionToDispose = null;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                sessionToDispose = _session;
                _session = null;
            }
            finally
            {
                _gate.Release();
            }

            if (sessionToDispose != null)
            {
                await sessionToDispose.DisposeAsync().ConfigureAwait(false);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalTerminalSessionManager));
            }
        }
    }

    private sealed class PtyBackedLocalTerminalSession : ILocalTerminalSession
    {
        private const int OutputReadBufferSize = 1024;
        private const int MaxBufferedOutputCharacters = 1_000_000;

        private readonly object _gate = new();
        private readonly SemaphoreSlim _inputGate = new(1, 1);
        private readonly IPtyConnection _connection;
        private readonly StreamReader _outputReader;
        private readonly StreamWriter _inputWriter;
        private readonly List<string> _outputBuffer = new();
        private int _bufferedOutputCharacters;
        private bool _canAcceptInput;
        private bool _disposed;
        private EventHandler<string>? _outputReceived;

        public PtyBackedLocalTerminalSession(
            string conversationId,
            string currentWorkingDirectory,
            IPtyConnection connection)
        {
            ConversationId = conversationId;
            CurrentWorkingDirectory = currentWorkingDirectory;
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _outputReader = new StreamReader(
                _connection.ReaderStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: OutputReadBufferSize,
                leaveOpen: true);
            _inputWriter = new StreamWriter(
                _connection.WriterStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: OutputReadBufferSize,
                leaveOpen: true)
            {
                AutoFlush = true
            };
        }

        public string ConversationId { get; }

        public string CurrentWorkingDirectory { get; }

        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;

        public bool CanAcceptInput
        {
            get
            {
                lock (_gate)
                {
                    return _canAcceptInput && !_disposed;
                }
            }
        }

        public event EventHandler<string>? OutputReceived
        {
            add
            {
                if (value is null)
                {
                    return;
                }

                string[] bufferedOutput;
                lock (_gate)
                {
                    _outputReceived += value;
                    bufferedOutput = _outputBuffer.ToArray();
                }

                foreach (var output in bufferedOutput)
                {
                    value(this, output);
                }
            }

            remove
            {
                lock (_gate)
                {
                    _outputReceived -= value;
                }
            }
        }

        public event EventHandler? StateChanged;

        public void AttachToRunningConnection()
        {
            _connection.ProcessExited += OnProcessExited;

            lock (_gate)
            {
                _canAcceptInput = true;
            }

            _ = PumpOutputAsync();
        }

        public async ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!CanAcceptInput)
            {
                throw new InvalidOperationException("Local terminal session is not accepting input.");
            }

            await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _inputWriter.WriteAsync(input).ConfigureAwait(false);
                await _inputWriter.FlushAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                MarkInputUnavailable();
                throw new InvalidOperationException("Local terminal session is not accepting input.");
            }
            catch (IOException)
            {
                MarkInputUnavailable();
                throw new InvalidOperationException("Local terminal session is not accepting input.");
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (columns <= 0 || rows <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    "Terminal size must be positive.");
            }

            _connection.Resize(columns, rows);
            return default;
        }

        public ValueTask DisposeAsync()
        {
            bool shouldRaiseStateChanged;

            lock (_gate)
            {
                if (_disposed)
                {
                    return default;
                }

                _disposed = true;
                shouldRaiseStateChanged = _canAcceptInput;
                _canAcceptInput = false;
            }

            _connection.ProcessExited -= OnProcessExited;

            try
            {
                _inputWriter.Dispose();
            }
            catch
            {
            }

            try
            {
                _outputReader.Dispose();
            }
            catch
            {
            }
            finally
            {
                try
                {
                    _connection.Kill();
                }
                catch
                {
                }

                try
                {
                    _connection.Dispose();
                }
                catch
                {
                }
                _inputGate.Dispose();
            }

            if (shouldRaiseStateChanged)
            {
                RaiseStateChanged();
            }

            return default;
        }

        private async Task PumpOutputAsync()
        {
            var buffer = new char[OutputReadBufferSize];
            try
            {
                while (true)
                {
                    var read = await _outputReader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        MarkInputUnavailable();
                        return;
                    }

                    PublishOutput(new string(buffer, 0, read));
                }
            }
            catch (ObjectDisposedException)
            {
                MarkInputUnavailable();
            }
            catch (IOException)
            {
                MarkInputUnavailable();
            }
            catch
            {
                MarkInputUnavailable();
            }
        }

        private void OnProcessExited(object? sender, PtyExitedEventArgs args)
        {
            MarkInputUnavailable();
        }

        private void PublishOutput(string? data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            var output = data;
            EventHandler<string>? handler;
            lock (_gate)
            {
                _outputBuffer.Add(output);
                _bufferedOutputCharacters += output.Length;
                TrimOutputBuffer();
                handler = _outputReceived;
            }

            handler?.Invoke(this, output);
        }

        private void TrimOutputBuffer()
        {
            while (_bufferedOutputCharacters > MaxBufferedOutputCharacters && _outputBuffer.Count > 0)
            {
                _bufferedOutputCharacters -= _outputBuffer[0].Length;
                _outputBuffer.RemoveAt(0);
            }
        }

        private void MarkInputUnavailable()
        {
            if (!TryTransitionToInputUnavailable())
            {
                return;
            }

            RaiseStateChanged();
        }

        private bool TryTransitionToInputUnavailable()
        {
            lock (_gate)
            {
                if (_disposed || !_canAcceptInput)
                {
                    return false;
                }

                _canAcceptInput = false;
                return true;
            }
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
