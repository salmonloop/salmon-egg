using System;
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
        private readonly string _command;
        private readonly string[] _args;
        private readonly Encoding _encoding;
        private readonly string _workingDirectory;
        private bool _disposed;
        private readonly object _lock = new();

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
        public StdioTransport(
            string command,
            string[]? args = null,
            Encoding? encoding = null)
        {
            // 去除命令和参数中的首尾空格（避免意外输入导致找不到文件）
            string trimmedCommand = (command ?? string.Empty).Trim();
            string[] trimmedArgs = args?.Select(a => a.Trim()).ToArray() ?? Array.Empty<string>();

            // 解析命令并处理 .cmd/.bat 脚本
            string resolvedCommand = StdioCommandResolver.Resolve(trimmedCommand);

            // 如果是 .cmd 或 .bat 文件，需要通过 cmd.exe 执行
            if (resolvedCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                resolvedCommand.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                _command = "cmd.exe";
                _args = new[] { "/c", resolvedCommand }.Concat(trimmedArgs).ToArray();
                _workingDirectory = ResolveWorkingDirectory(resolvedCommand);
            }
            else
            {
                _command = resolvedCommand;
                _args = trimmedArgs;
                _workingDirectory = ResolveWorkingDirectory(resolvedCommand);
            }

            _encoding = NormalizeTransportEncoding(encoding ?? Encoding.UTF8);
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

            try
            {
                _readCts = new CancellationTokenSource();

                var processInfo = CreateProcessStartInfo();

                _process = new Process { StartInfo = processInfo };
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;

                _logger.Information("[StdioTransport.Connect] Starting process. Command={Command} ArgsCount={ArgsCount}", _command, processInfo.ArgumentList.Count);

                // 在后台启动进程，避免阻塞 UI 线程
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        _process.Start();
                        _logger.Information("[StdioTransport.Connect] Process started. PID={Pid}", _process.Id);
                    }
                }, cancellationToken).ConfigureAwait(false);

                _stdin = _process.StandardInput;
                _stdout = _process.StandardOutput;
                _stderr = _process.StandardError;

                // 读循环必须在启动观察窗之前开始消费两条管道:
                // 子进程启动即写满 stdout/stderr 管道缓冲时会阻塞在写端,
                // 既无法继续启动也无法退出,观察窗判定与快速失败诊断都会失真。
                _ = ReadLoopAsync(_readCts.Token);
                _ = ReadErrorLoopAsync(_readCts.Token);

                await Task.Delay(StartupObservationTimeout, cancellationToken).ConfigureAwait(false);

                if (!_process.HasExited)
                {
                    IsConnected = true;

                    _logger.Information("[StdioTransport.Connect] Connected. PID={Pid}", _process.Id);
                    return true;
                }
                else
                {
                    _logger.Warning("[StdioTransport.Connect] Process exited. ExitCode={ExitCode}", _process.ExitCode);
                    OnErrorOccurred(new TransportErrorEventArgs(
                        $"Process exited immediately after start. ExitCode={_process.ExitCode}",
                        kind: TransportErrorKind.ProcessStartFailed));
                    return false;
                }
            }
            catch (Exception ex)
            {
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
                FileName = _command,
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

            foreach (var argument in _args)
            {
                processInfo.ArgumentList.Add(argument);
            }

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
                        process.Kill();
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
                _logger.Information("[StdioTransport.SendMessage] Sending message. Length={Length}", message.Length);

                // 发送消息后添加换行符
                await _stdin.WriteAsync(message + Environment.NewLine).ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] Wrote stdin; flushing...");

                await _stdin.FlushAsync().ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] Flush completed");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.SendMessage] Send failed");
                OnErrorOccurred(new TransportErrorEventArgs(
                    $"Failed to send message: {ex.Message}",
                    ex,
                    TransportErrorKind.SendFailed));
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 读取输出循环。
        /// </summary>
        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information("[StdioTransport.ReadLoop] Starting. PID={Pid}", _process?.Id);
                int lineCount = 0;
                // 移除 _stdout.EndOfStream 检查，因为它是一个同步阻塞属性，会导致 ConnectAsync 被阻塞
                while (!cancellationToken.IsCancellationRequested && _stdout != null)
                {
                    var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        _logger.Warning("[StdioTransport.ReadLoop] ReadLine returned null; stream may have ended");
                        break;
                    }
                    lineCount++;
                    _logger.Debug("[StdioTransport.ReadLoop] Received line {Count}. Length={Length}", lineCount, line.Length);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Debug("[StdioTransport.ReadLoop] Raising OnMessageReceived. Line={Count}, Length={Length}", lineCount, line.Length);
                        OnMessageReceived(new MessageReceivedEventArgs(line));
                    }
                    else
                    {
                        _logger.Debug("[StdioTransport.ReadLoop] Ignoring empty line");
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

                while (!cancellationToken.IsCancellationRequested && _stderr != null)
                {
                    var line = await _stderr.ReadLineAsync().ConfigureAwait(false);
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

            foreach (var fallback in GetFallbackWorkingDirectoryCandidates())
            {
                if (IsSafeWorkingDirectory(fallback, ensureExists: true))
                {
                    return Path.GetFullPath(fallback);
                }
            }

            return Path.GetTempPath();
        }

        private static bool IsSafeWorkingDirectory(string? path, bool ensureExists = false)
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

                if (ensureExists)
                {
                    Directory.CreateDirectory(fullPath);
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
        /// 进程退出事件处理。不取消读循环:让其自然读到 EOF,
        /// 否则崩溃前最后写入的 stdout/stderr(通常是失败原因)会被截断。
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            IsConnected = false;
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
            CloseStandardInput(stdin);
            if (began && process is { HasExited: false })
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // 进程可能已在退出途中。
                }
            }

            stdout?.Dispose();
            stderr?.Dispose();
            process?.Dispose();
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
                if (!IsConnected)
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
