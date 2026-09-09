using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class TerminalAuthenticationSessionFactory : ITerminalAuthenticationSessionFactory, IAsyncDisposable
{
    private readonly IPlatformCapabilityService _platform;
    private readonly Func<PtyOptions, CancellationToken, Task<IPtyConnection>> _spawn;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _lifetimeGate = new(1, 1);
    private readonly object _gate = new();
    private readonly HashSet<AuthenticationSession> _sessions = new();
    private Task? _dispose;

    public TerminalAuthenticationSessionFactory(IPlatformCapabilityService platform)
        : this(platform, PtyProvider.SpawnAsync)
    {
    }

    internal TerminalAuthenticationSessionFactory(
        IPlatformCapabilityService platform,
        Func<PtyOptions, CancellationToken, Task<IPtyConnection>> spawn)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
    }

    public bool IsSupported => _platform.SupportsTerminalAuthentication;

    public async ValueTask<ITerminalAuthenticationSession> StartAsync(
        StdioInvocationSnapshot invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Interactive agent sign-in is not available on this platform.");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _lifetimeGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            return await StartCoreAsync(invocation, lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _dispose ??= DisposeCoreAsync();
            return new ValueTask(_dispose);
        }
    }

    private async Task<ITerminalAuthenticationSession> StartCoreAsync(
        StdioInvocationSnapshot invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await _spawn(new PtyOptions
        {
            Name = "SalmonEgg agent sign-in",
            App = invocation.Command,
            CommandLine = invocation.Arguments.ToArray(),
            Cwd = invocation.WorkingDirectory,
            Environment = new Dictionary<string, string>(invocation.Environment),
            Cols = 100,
            Rows = 28
        }, cancellationToken).ConfigureAwait(false);
        AuthenticationSession? session = null;
        try
        {
            session = new AuthenticationSession(connection, RemoveSession);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _sessions.Add(session);
            return session;
        }
        catch
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            else await Task.Run(() => CloseNativeConnection(connection)).ConfigureAwait(false);
            throw;
        }
    }

    private void RemoveSession(AuthenticationSession session)
    {
        lock (_gate) _sessions.Remove(session);
    }

    private async Task DisposeCoreAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _lifetimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            AuthenticationSession[] sessions;
            lock (_gate) sessions = _sessions.ToArray();
            await Task.WhenAll(sessions.Select(static session => session.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    private static void CloseNativeConnection(IPtyConnection connection)
    {
        try
        {
            connection.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process may already have exited; closing the Windows job still reclaims descendants.
        }
        finally
        {
            connection.Dispose();
        }
    }

    private sealed class AuthenticationSession : ITerminalAuthenticationSession
    {
        private const int MaximumBufferedCharacters = 64 * 1024;
        private readonly IPtyConnection _connection;
        private readonly Action<AuthenticationSession> _onDisposed;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _inputGate = new(1, 1);
        private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private readonly object _outputGate = new();
        private readonly Queue<string> _output = new();
        private readonly Task _pump;
        private Task? _dispose;
        private EventHandler<string>? _outputReceived;
        private int _outputCharacters;
        private bool _closed;

        public AuthenticationSession(IPtyConnection connection, Action<AuthenticationSession> onDisposed)
        {
            _connection = connection;
            _onDisposed = onDisposed;
            _reader = new StreamReader(connection.ReaderStream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            _writer = new StreamWriter(connection.WriterStream, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            connection.ProcessExited += OnExited;
            // Subscribe first: an extremely fast process can exit before SpawnAsync returns.
            if (connection.WaitForExit(0))
            {
                Complete(connection.ExitCode);
            }

            _pump = PumpAsync();
        }

        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;

        public bool CanAcceptInput
        {
            get { lock (_gate) { return !_closed && !_completion.Task.IsCompleted; } }
        }

        public Task<int?> Completion => _completion.Task;

        public event EventHandler? StateChanged;

        public event EventHandler<string>? OutputReceived
        {
            add
            {
                if (value is null) return;
                lock (_outputGate)
                {
                    _outputReceived += value;
                    try
                    {
                        foreach (var output in _output.ToArray()) value(this, output);
                    }
                    catch (Exception)
                    {
                        // A detached/failed view is not a successful login. Keep the native reader
                        // alive until the owner disposes the session and closes its process tree.
                        _completion.TrySetResult(null);
                        _outputReceived -= value;
                    }
                }
            }
            remove { lock (_outputGate) { _outputReceived -= value; } }
        }

        public async ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!CanAcceptInput) throw new InvalidOperationException("The sign-in terminal has closed.");
                await _writer.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            lock (_gate)
            {
                if (!_closed) _connection.Resize(columns, rows);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                _closed = true;
                _dispose ??= DisposeCoreAsync();
                return new ValueTask(_dispose);
            }
        }

        private async Task DisposeCoreAsync()
        {
            _connection.ProcessExited -= OnExited;
            _completion.TrySetResult(null);
            // On Windows, disposing the native connection closes its JobObject with
            // KILL_ON_JOB_CLOSE, reclaiming descendants as well as the original process.
            try
            {
                await Task.Run(() => CloseNativeConnection(_connection)).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _pump.ConfigureAwait(false);
                    await _inputGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        _reader.Dispose();
                        try { _writer.Dispose(); }
                        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                        {
                            // Native teardown already closed the pipe; StreamWriter's final flush
                            // cannot succeed after that and is not an authentication failure.
                        }
                    }
                    finally { _inputGate.Release(); }
                }
                finally
                {
                    lock (_outputGate) { _output.Clear(); _outputReceived = null; _outputCharacters = 0; }
                    _onDisposed(this);
                }
            }
        }

        private async Task PumpAsync()
        {
            var buffer = new char[1024];
            try
            {
                while (await _reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) is var length && length > 0)
                {
                    var output = new string(buffer, 0, length);
                    lock (_outputGate)
                    {
                        // ClosePseudoConsole can wait for its output pipe to drain. During teardown
                        // keep consuming native output, but stop publishing it to the detached view.
                        if (Volatile.Read(ref _closed)) continue;
                        _output.Enqueue(output);
                        _outputCharacters += output.Length;
                        while (_outputCharacters > MaximumBufferedCharacters)
                            _outputCharacters -= _output.Dequeue().Length;
                        try { _outputReceived?.Invoke(this, output); }
                        catch (Exception)
                        {
                            _completion.TrySetResult(null);
                            _outputReceived = null;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException
                || (Volatile.Read(ref _closed) && ex is InvalidOperationException))
            {
                // Pipe closure is expected during native teardown. No terminal output is logged.
            }
            finally
            {
                // EOF is not evidence of authentication success. Only the process exit result is.
                // WaitForExit catches an exit whose event has not yet reached us; a live process that
                // closes its terminal stream has no interactive path and must fail closed.
                if (!Volatile.Read(ref _closed) && !_completion.Task.IsCompleted)
                {
                    try
                    {
                        if (_connection.WaitForExit(0)) Complete(_connection.ExitCode);
                        else _completion.TrySetResult(null);
                    }
                    catch (InvalidOperationException) when (Volatile.Read(ref _closed))
                    {
                        _completion.TrySetResult(null);
                    }
                }
            }
        }

        private void OnExited(object? sender, PtyExitedEventArgs args) => Complete(args.ExitCode);

        private void Complete(int exitCode)
        {
            if (_completion.TrySetResult(exitCode)) StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
