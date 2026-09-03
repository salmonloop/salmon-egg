using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Infrastructure.Transport
{
    /// <summary>
    /// Stdio 传输层实现。
    /// 通过标准输入/输出与 Agent 进程通信。
    /// </summary>
    public class StdioTransport : ITransport, IDisposable
    {
        private static readonly ILogger _logger = Log.ForContext<StdioTransport>();

        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private StreamReader? _stderr;
        private CancellationTokenSource? _readCts;
        private static readonly TimeSpan StartupObservationTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly LauncherInvocation _invocation;
        private readonly Encoding _encoding;
        private readonly string _workingDirectory;
        private readonly IReadOnlyDictionary<string, string> _environment;
        private bool _disposed;
        private readonly object _lock = new();

        // Serializes writes to the single stdin StreamWriter. A StreamWriter forbids concurrent
        // async writes (throws ThrowAsyncIOInProgress); multiple in-flight ACP requests (e.g. a
        // session/list and a session/load racing on the shared client) would otherwise overlap.
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        /// <summary>
        /// 消息接收事件。
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// 传输错误事件。
        /// </summary>
        public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// 判断传输是否已连接。
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 创建新的 StdioTransport 实例。
        /// </summary>
        /// <param name="command">Agent 可执行文件的命令</param>
        /// <param name="args">命令行参数</param>
        /// <param name="encoding">字符编码</param>
        /// <param name="environment">
        /// 附加到子进程环境的变量。叠加在继承环境之上，null 或空表示不修改环境。
        /// </param>
        public StdioTransport(
            string command,
            string[]? args = null,
            Encoding? encoding = null,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            // 命令解析、.cmd/.bat 包装与启动器目录上 PATH 都由 LauncherInvocation 统一负责，
            // 使 ACP 向导的探测/安装与本传输走同一套规则。解析用配置环境覆盖后的生效 PATH，
            // 与 ApplyTo 写入子进程的执行 PATH 同源，预检结论因此与启动行为一致。
            _environment = NormalizeEnvironment(environment);
            _invocation = LauncherInvocation.Create(command, args, _environment);
            _workingDirectory = ResolveWorkingDirectory(_invocation.ResolvedCommand);

            _encoding = NormalizeTransportEncoding(encoding ?? Encoding.UTF8);
        }

        /// <summary>
        /// 复制并去掉空名条目，使传输持有自己的不可变副本，避免调用方后续修改影响已建连的进程。
        /// </summary>
        private static IReadOnlyDictionary<string, string> NormalizeEnvironment(
            IReadOnlyDictionary<string, string>? environment)
        {
            if (environment is null || environment.Count == 0)
            {
                return EmptyEnvironment;
            }

            var normalized = new Dictionary<string, string>(environment.Count, StringComparer.Ordinal);
            foreach (var entry in environment)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                normalized[entry.Key.Trim()] = entry.Value ?? string.Empty;
            }

            return normalized;
        }

        private static Encoding NormalizeTransportEncoding(Encoding encoding)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            // ACP JSON-RPC over stdio must not prepend UTF-8 BOM; otherwise first frame parsing fails.
            if (string.Equals(encoding.WebName, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase) &&
                encoding.GetPreamble().Length > 0)
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            return encoding;
        }

        /// <summary>
        /// 建立与 Agent 的连接。
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                return true;
            }

            // Held locally until Start() succeeds; see the publication comment below.
            Process? starting = null;

            try
            {
                // 在启动进程前按同一份解析结果自查：命令根本不存在时，给用户一句能照着做的报错，
                // 而不是转发 CreateProcess 的原始 Win32 异常。纯只读判定（解析期已判完），不 spawn。
                if (StdioCommandPreflight.BuildMissingCommandError(_invocation, OperatingSystem.IsWindows()) is { } preflightError)
                {
                    _logger.Warning(
                        "Agent command not found during preflight. Command={Command} SearchedDirectories={SearchedDirectories}",
                        _invocation.ResolvedCommand,
                        _invocation.SearchedDirectories);
                    OnErrorOccurred(new TransportErrorEventArgs(preflightError, kind: TransportErrorKind.ProcessStartFailed));
                    return false;
                }

                _readCts = new CancellationTokenSource();

                var processInfo = CreateProcessStartInfo();

                starting = new Process { StartInfo = processInfo };
                starting.EnableRaisingEvents = true;
                starting.Exited += OnProcessExited;

                _logger.Information("[StdioTransport.Connect] Starting process. Command={Command} ArgsCount={ArgsCount}", _invocation.FileName, processInfo.ArgumentList.Count);

                // 在后台启动进程，避免阻塞 UI 线程
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        starting.Start();

                        // Published only once Start() has returned. Teardown reads this field to decide
                        // it owns a child that needs reaping, so the field has to mean exactly that: a
                        // Process whose Start() threw owns no child, and reading HasExited on it throws
                        // InvalidOperationException("No process is associated with this object") — from
                        // a pattern match that sits outside the try guarding the kill, so it escapes
                        // Dispose entirely. Publishing after the start makes "field is set" equal to
                        // "a child exists" by construction, which is what lets both kill sites stay
                        // free of defensive checks.
                        _process = starting;
                        _logger.Information("[StdioTransport.Connect] Process started. PID={Pid}", starting.Id);
                    }
                }, cancellationToken).ConfigureAwait(false);

                // Everything below works off the local, not the field: teardown clears _process when it
                // takes ownership, so a disconnect arriving mid-connect would otherwise turn these into
                // a NullReferenceException. Reading the local can still throw once teardown has
                // disposed the Process, which the catch below reports as a failed start — the right
                // answer for a connect that raced a disconnect.
                _stdin = starting.StandardInput;
                _stdout = starting.StandardOutput;
                _stderr = starting.StandardError;

                // 读循环必须在启动观察窗之前开始消费两条管道:
                // 子进程启动即写满 stdout/stderr 管道缓冲时会阻塞在写端,
                // 既无法继续启动也无法退出,观察窗判定与快速失败诊断都会失真。
                _ = ReadLoopAsync(_readCts.Token);
                _ = ReadErrorLoopAsync(_readCts.Token);

                await Task.Delay(StartupObservationTimeout, cancellationToken).ConfigureAwait(false);

                if (!starting.HasExited)
                {
                    IsConnected = true;

                    _logger.Information("[StdioTransport.Connect] Connected. PID={Pid}", starting.Id);
                    return true;
                }
                else
                {
                    _logger.Warning("[StdioTransport.Connect] Process exited. ExitCode={ExitCode}", starting.ExitCode);
                    OnErrorOccurred(new TransportErrorEventArgs(
                        $"Process exited immediately after start. ExitCode={starting.ExitCode}",
                        kind: TransportErrorKind.ProcessStartFailed));
                    return false;
                }
            }
            catch (Exception ex)
            {
                // If Start() failed before publication, teardown cannot see this Process and nothing
                // else will release it. Once it has been published, teardown owns the child and can
                // clear _process before this catch runs; a second Dispose() of the handle is harmless.
                if (_process is null)
                {
                    starting?.Dispose();
                }

                _logger.Error(ex, "[StdioTransport.Connect] Start failed");
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Unable to start process: {ex.Message}",
                    ex,
                    TransportErrorKind.ProcessStartFailed));
                return false;
            }
        }

        internal ProcessStartInfo CreateProcessStartInfo()
        {
            var processInfo = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = _encoding,
                StandardOutputEncoding = _encoding,
                StandardErrorEncoding = _encoding,
                WorkingDirectory = _workingDirectory
            };

            // Applied after construction so configured values win over the inherited environment.
            foreach (var entry in _environment)
            {
                processInfo.Environment[entry.Key] = entry.Value;
            }

            // Last, so the launcher's own directory extends whatever PATH the configured environment
            // settled on rather than the inherited one it may have replaced.
            _invocation.ApplyTo(processInfo);

            return processInfo;
        }

        /// <summary>
        /// 断开与 Agent 的连接。状态声明在锁内完成,kill/等待/释放等阻塞 IO 移出锁,
        /// 避免持锁做进程 IO 与 Send/Connect 路径互相卡死。
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            if (!TryBeginTeardown(out var process, out var stdin, out var stdout, out var stderr))
            {
                return true;
            }

            try
            {
                CloseStandardInput(stdin);
                if (process is { HasExited: false })
                {
                    try
                    {
                        // entireProcessTree, because the child is often not the agent. A Windows agent
                        // installed via npm resolves to a .CMD (see StdioCommandResolver's PATHEXT
                        // default) and is launched through cmd.exe /c, which makes the real agent a
                        // grandchild; killing only the direct child leaves it running and holding the
                        // session's resources. Measured with a shell wrapping a long sleep: the plain
                        // overload leaves one of two processes alive, this one reaps both.
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // 如果进程无法终止，继续执行
                    }
                }

                stdout?.Dispose();
                stderr?.Dispose();
                process?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Error while disconnecting: {ex.Message}",
                    ex,
                    TransportErrorKind.DisconnectFailed));
                return false;
            }
        }

        /// <summary>
        /// 发送消息。
        /// </summary>
        public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            // 检查连接状态（不使用锁，避免死锁）
            if (!IsConnected || _stdin == null)
            {
                _logger.Warning("[StdioTransport.SendMessage] Failed: not connected or _stdin is null");
                OnErrorOccurred(new TransportErrorEventArgs(
                    "Transport is not connected",
                    kind: TransportErrorKind.NotConnected));
                return false;
            }

            // 检查进程是否已退出
            if (_process != null && _process.HasExited)
            {
                _logger.Error("[StdioTransport.SendMessage] Failed: process exited. ExitCode={ExitCode}", _process.ExitCode);
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Agent process exited. ExitCode={_process.ExitCode}",
                    kind: TransportErrorKind.ProcessExited));
                IsConnected = false;
                return false;
            }

            try
            {
                await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Raced with Dispose: the transport is gone. Treat as not connected.
                return false;
            }

            try
            {
                _logger.Information("[StdioTransport.SendMessage] Sending message. Length={Length}", message.Length);

                // 发送消息后添加换行符
                await _stdin.WriteAsync((message + Environment.NewLine).AsMemory(), cancellationToken).ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] Wrote stdin; flushing...");

                await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] Flush completed");

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller-initiated cancellation is not a transport fault; do not tear the pipe down.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.SendMessage] Send failed");
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Failed to send message: {ex.Message}",
                    ex,
                    TransportErrorKind.SendFailed));

                // Only a genuinely broken pipe / dead process is a permanent disconnect. A transient
                // write conflict (e.g. InvalidOperationException from an overlapping write) is
                // recoverable and must not zombify the transport or cancel all in-flight requests.
                if (IsFatalSendFailure(ex))
                {
                    IsConnected = false;
                }

                return false;
            }
            finally
            {
                // Guard against a Dispose that raced in while this send held the gate.
                try { _sendGate.Release(); }
                catch (ObjectDisposedException) { }
            }
        }

        // A send fault is fatal only when the underlying pipe/process is actually gone. IO errors and
        // a confirmed process exit are terminal; everything else (notably InvalidOperationException from
        // a transient stream-in-use overlap) is recoverable and leaves the connection intact.
        private bool IsFatalSendFailure(Exception ex)
            => ex is IOException
               || ex is ObjectDisposedException
               || (_process is { HasExited: true });

        /// <summary>
        /// 读取输出循环。
        /// </summary>
        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information("[StdioTransport.ReadLoop] Starting. PID={Pid}", _process?.Id);
                int lineCount = 0;

                // Captured once: teardown clears _stdout when it takes ownership, so re-reading the
                // field each iteration would race — the null check could pass and the field be
                // cleared before the read. The loop ends on cancellation or end of stream instead.
                var stdout = _stdout;
                if (stdout is null)
                {
                    return;
                }

                // 移除 _stdout.EndOfStream 检查，因为它是一个同步阻塞属性，会导致 ConnectAsync 被阻塞
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Pass the token: the parameterless overload cannot be interrupted, so a
                    // deliberate teardown disposes the reader out from under a parked read and
                    // surfaces IOException("Operation canceled") — an OS errno message that reads
                    // like a fault and was reported as StdoutReadFailed. With the token the parked
                    // read throws OperationCanceledException instead, which the catch below already
                    // treats as the expected end of a teardown.
                    var line = await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null)
                    {
                        _logger.Warning("[StdioTransport.ReadLoop] ReadLine returned null; stream may have ended");
                        break;
                    }
                    lineCount++;
                    _logger.Debug("[StdioTransport.ReadLoop] Received line {Count}. Length={Length}", lineCount, line.Length);

                    switch (ClassifyStdoutLine(line, out var frame))
                    {
                        case StdoutFrameKind.Frame:
                            _logger.Debug("[StdioTransport.ReadLoop] Raising OnMessageReceived. Line={Count}, Length={Length}", lineCount, frame.Length);
                            OnMessageReceived(new MessageReceivedEventArgs(frame));
                            break;

                        case StdoutFrameKind.Diagnostic:
                            // Warning (not Debug) so the offending content is visible at the default
                            // log level; without it every cause reduces to the same parser message.
                            var described = AcpFrame.Describe(line);
                            _logger.Warning(
                                "[StdioTransport.ReadLoop] Ignoring non-ACP stdout line {Count}. The agent must write diagnostics to stderr. Content={Content}",
                                lineCount,
                                described);
                            OnErrorOccurred(new TransportErrorEventArgs(
                                $"Agent wrote non-ACP output to stdout: {described}",
                                kind: TransportErrorKind.StdoutProtocolViolation));
                            break;

                        default:
                            _logger.Debug("[StdioTransport.ReadLoop] Ignoring empty line");
                            break;
                    }
                }
                _logger.Warning("[StdioTransport.ReadLoop] Ended after {Count} lines. Cancelled={Cancelled}",
                    lineCount, cancellationToken.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                _logger.Verbose("[StdioTransport.ReadLoop] Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.ReadLoop] Failed");
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Failed to read process output: {ex.Message}",
                    ex,
                    TransportErrorKind.StdoutReadFailed));
            }
        }

        /// <summary>
        /// 读取错误循环。
        /// </summary>
        private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information("[StdioTransport.ReadError] Starting. PID={Pid}", _process?.Id);

                // Captured once, for the same reason as the stdout loop.
                var stderr = _stderr;
                if (stderr is null)
                {
                    return;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Token-aware for the same reason as the stdout loop: a deliberate teardown must
                    // end this read via cancellation, not via a disposed-stream IOException.
                    var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) break;
                    _logger.Verbose("[StdioTransport.ReadError] Received stderr line. Length={Length}", line.Length);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Warning("[StdioTransport.ReadError] Non-empty process stderr. Length={Length}", line.Length);
                        OnErrorOccurred(new TransportErrorEventArgs(
                            $"Process error: {line}",
                            kind: TransportErrorKind.AgentStderr));
                    }
                }
                _logger.Warning("[StdioTransport.ReadError] Ended");
            }
            catch (OperationCanceledException)
            {
                _logger.Verbose("[StdioTransport.ReadError] Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.ReadError] Failed");
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Failed to read process error stream: {ex.Message}",
                    ex,
                    TransportErrorKind.StderrReadFailed));
            }
        }

        /// <summary>
        /// How a raw stdout line should be treated by the read loop.
        /// </summary>
        internal enum StdoutFrameKind
        {
            /// <summary>Nothing to dispatch (blank, or a lone byte order mark).</summary>
            Blank,

            /// <summary>Looks like an ACP frame; hand it to the JSON-RPC layer.</summary>
            Frame,

            /// <summary>
            /// Never looked like an ACP frame. Per ACP the agent MUST NOT write this to stdout,
            /// so it is agent diagnostics on the wrong stream rather than a protocol error.
            /// </summary>
            Diagnostic
        }

        /// <summary>
        /// Classifies one raw stdout line and, for frames, returns the text to dispatch.
        /// </summary>
        /// <remarks>
        /// The frame test itself lives in <see cref="AcpFrame"/> so every transport shares one
        /// definition; what is stdio-specific is the third outcome. Only here is there a stderr to
        /// contrast with, so only here can a non-frame be attributed to the agent writing
        /// diagnostics to the stream ACP reserves for the protocol.
        /// </remarks>
        internal static StdoutFrameKind ClassifyStdoutLine(string? line, out string frame)
        {
            frame = string.Empty;

            if (AcpFrame.IsBlank(line))
            {
                return StdoutFrameKind.Blank;
            }

            if (!AcpFrame.LooksLikeFrame(line))
            {
                return StdoutFrameKind.Diagnostic;
            }

            frame = AcpFrame.StripByteOrderMark(line!);
            return StdoutFrameKind.Frame;
        }

        internal static string ResolveWorkingDirectory(string resolvedCommand, string? currentDirectory = null)
        {
            if (!string.IsNullOrWhiteSpace(resolvedCommand)
                && Path.IsPathRooted(resolvedCommand))
            {
                var commandDirectory = Path.GetDirectoryName(resolvedCommand);
                if (IsSafeWorkingDirectory(commandDirectory))
                {
                    return commandDirectory!;
                }
            }

            var candidate = string.IsNullOrWhiteSpace(currentDirectory)
                ? Environment.CurrentDirectory
                : currentDirectory;
            if (IsSafeWorkingDirectory(candidate))
            {
                return Path.GetFullPath(candidate!);
            }

            // Fallback candidates are resolved without side effects. The constructor
            // must not create directories (AGENTS.md cache/persistence boundary:
            // ctors/getters/VM-init/DI may not trigger real FS writes); an absent
            // candidate simply falls through to the next existing one. The app's
            // own LocalAppData/SalmonEgg root is created lazily by the services that
            // actually own it (StorageLocationService / LoggingConfiguration etc.),
            // not by transport construction.
            foreach (var fallback in GetFallbackWorkingDirectoryCandidates())
            {
                if (IsSafeWorkingDirectory(fallback))
                {
                    return Path.GetFullPath(fallback);
                }
            }

            return Path.GetTempPath();
        }

        private static bool IsSafeWorkingDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (IsWindowsAppsPath(fullPath))
                {
                    return false;
                }

                if (!Directory.Exists(fullPath))
                {
                    return false;
                }

                _ = File.GetAttributes(fullPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWindowsAppsPath(string fullPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            var windowsAppsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");

            return fullPath.StartsWith(windowsAppsRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] GetFallbackWorkingDirectoryCandidates()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return
            [
                Path.Combine(localAppData, "SalmonEgg"),
                localAppData,
                userProfile,
                Path.GetTempPath()
            ];
        }

        /// <summary>
        /// True once teardown has begun. <see cref="TryBeginTeardown"/> cancels the read token
        /// before killing the process, so this distinguishes "we ended this" from "it died on us"
        /// without introducing a second piece of state to keep in sync.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="CancellationTokenSource.IsCancellationRequested"/> rather than
        /// <c>Token</c>: the former keeps returning the last state after the source is disposed,
        /// whereas the latter throws <see cref="ObjectDisposedException"/>. Teardown disposes the
        /// source, and the process-exit callback can arrive around that.
        /// </remarks>
        private bool IsTearingDown => _readCts?.IsCancellationRequested ?? false;

        /// <summary>
        /// 进程退出事件处理。不取消读循环:让其自然读到 EOF,
        /// 否则崩溃前最后写入的 stdout/stderr(通常是失败原因)会被截断。
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            IsConnected = false;

            // We kill the agent during teardown, so reporting its exit as an error there blames the
            // agent for something we did — and it reaches the user, because a deliberate disconnect
            // happens while listeners are still attached. An exit we did not ask for is still a
            // fault and still reported.
            if (IsTearingDown)
            {
                _logger.Information("[StdioTransport.ProcessExited] Agent process exited during teardown");
                return;
            }

            OnErrorOccurred(new TransportErrorEventArgs(
                "Agent process exited",
                kind: TransportErrorKind.ProcessExited));
        }

        /// <summary>
        /// 触发消息接收事件。
        /// </summary>
        protected virtual void OnMessageReceived(MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// 触发错误事件。
        /// </summary>
        protected virtual void OnErrorOccurred(TransportErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        /// <summary>
        /// 释放资源。Dispose 是同步契约:同步终止进程并释放句柄,不 fire-and-forget
        /// 在途异步断开;需要优雅等待退出的调用方应先 await <see cref="DisconnectAsync"/>。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var began = TryBeginTeardown(out var process, out var stdin, out var stdout, out var stderr);

            // Before the kill below, never after: killing first fires Process.Exited while
            // IsTearingDown still reads false, which reports an exit we are in the middle of causing.
            // TryBeginTeardown above already cancels on every path that returns true, so this is
            // belt-and-braces rather than the sole mechanism — kept unconditional so that the ordering
            // Dispose depends on cannot be broken by a later change to that method's gate, which is
            // how the spurious exit report got introduced the first time.
            _readCts?.Cancel();

            CloseStandardInput(stdin);

            // `began` now means "we took ownership and are the one reaping", which holds even when
            // IsConnected was never set — a connect that started the child and then failed or was
            // cancelled inside the startup observation window leaves it alive, and neither
            // process.Dispose() nor the GC terminates it (there is no finalizer;
            // GC.SuppressFinalize below). Whoever tears down second gets nulls and skips this.
            if (began && process is { HasExited: false })
            {
                try
                {
                    // entireProcessTree for the same reason as DisconnectAsync: the agent is a
                    // grandchild whenever a launcher sits in between.
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 进程可能已在退出途中。
                }
            }

            stdout?.Dispose();
            stderr?.Dispose();
            process?.Dispose();

            // Safe to release once cancellation has been signalled above: the loops hold the token by
            // value, and a captured token whose source is disposed still reports cancellation
            // correctly. The field is deliberately neither nulled nor reassigned — IsTearingDown
            // reads IsCancellationRequested off it, which keeps returning the last state after
            // disposal but would read false if the field were cleared, silently restoring the
            // spurious "Agent process exited" this class reports for an exit it caused itself.
            _readCts?.Dispose();

            _sendGate.Dispose();
            GC.SuppressFinalize(this);
        }

        private bool TryBeginTeardown(
            out Process? process,
            out StreamWriter? stdin,
            out StreamReader? stdout,
            out StreamReader? stderr)
        {
            lock (_lock)
            {
                process = _process;
                stdin = _stdin;
                stdout = _stdout;
                stderr = _stderr;

                // Hand over ownership: the caller now reaps and disposes these, so clear the fields.
                // Whoever arrives second gets nulls and does nothing, which is what makes teardown
                // single-shot without a separate "already torn down" flag to keep in sync. Reading
                // HasExited on an already-disposed Process throws, so the second caller must not see
                // it at all.
                //
                // Deliberately keyed on having something to tear down rather than on IsConnected: a
                // connect that started the child and then failed or was cancelled inside the startup
                // observation window never set IsConnected, and gating on it there left the child
                // alive with nobody to reap it.
                //
                // _readCts is NOT cleared — IsTearingDown reads IsCancellationRequested off it.
                _process = null;
                _stdin = null;
                _stdout = null;
                _stderr = null;

                if (process is null)
                {
                    return false;
                }

                IsConnected = false;
                _readCts?.Cancel();
                return true;
            }
        }

        private static void CloseStandardInput(StreamWriter? stdin)
        {
            try
            {
                stdin?.Flush();
                stdin?.Close();
                stdin?.Dispose();
            }
            catch
            {
                // teardown 阶段管道可能已断,关闭失败无碍。
            }
        }
    }
}
