using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Utilities;

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
        private readonly string _command;
        private readonly string[] _args;
        private readonly Encoding _encoding;
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

        // 调试文件追踪器
        private StreamWriter? _debugFileWriter;

        /// <summary>
        /// 创建新的 StdioTransport 实例。
        /// </summary>
        /// <param name="command">Agent 可执行文件的命令</param>
        /// <param name="args">命令行参数</param>
        /// <param name="encoding">字符编码</param>
        public StdioTransport(string command, string[]? args = null, Encoding? encoding = null)
        {
            // 去除命令和参数中的首尾空格（避免意外输入导致找不到文件）
            string trimmedCommand = (command ?? string.Empty).Trim();
            string[] trimmedArgs = args?.Select(a => a.Trim()).ToArray() ?? Array.Empty<string>();

            // 解析命令并处理 .cmd/.bat 脚本
            string resolvedCommand = PathResolver.ResolveCommand(trimmedCommand);

            // 如果是 .cmd 或 .bat 文件，需要通过 cmd.exe 执行
            if (resolvedCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                resolvedCommand.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                _command = "cmd.exe";
                _args = new[] { "/c", resolvedCommand }.Concat(trimmedArgs).ToArray();
            }
            else
            {
                _command = resolvedCommand;
                _args = trimmedArgs;
            }

            _encoding = encoding ?? Encoding.UTF8;
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

                // 初始化调试文件写入器
                try
                {
                    var debugFilePath = Path.Combine(Path.GetTempPath(), $"acp_transport_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    _debugFileWriter = new StreamWriter(debugFilePath, false, Encoding.UTF8);
                    _debugFileWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === ACP Transport Debug Log Started ===");
                    _debugFileWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Command: {_command} {string.Join(" ", _args)}");
                    _debugFileWriter.Flush();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "[StdioTransport.Connect] 无法创建调试文件");
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = _command,
                    Arguments = string.Join(" ", _args.Select(a => $"\"{a}\"")),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardInputEncoding = _encoding,
                    StandardOutputEncoding = _encoding,
                    StandardErrorEncoding = _encoding,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                _process = new Process { StartInfo = processInfo };
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;

                _logger.Information("[StdioTransport.Connect] 准备启动进程：{Command} {Args}", _command, processInfo.Arguments);
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CONNECT: Starting process {_command} {processInfo.Arguments}");
                _debugFileWriter?.Flush();

                // 在后台启动进程，避免阻塞 UI 线程
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        _process.Start();
                        _logger.Information("[StdioTransport.Connect] 进程已启动 PID={Pid}", _process.Id);
                    }
                }, cancellationToken).ConfigureAwait(false);

                // 等待进程真正启动（最多 5 秒）
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                if (!_process.HasExited)
                {
                    _stdin = _process.StandardInput;
                    _stdout = _process.StandardOutput;
                    _stderr = _process.StandardError;

                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CONNECT: Process started PID={_process.Id}");
                    _debugFileWriter?.Flush();

                    _logger.Information("[StdioTransport.Connect] 启动读取循环");

                    // 启动读取循环
                    _ = ReadLoopAsync(_readCts.Token);
                    _ = ReadErrorLoopAsync(_readCts.Token);

                    IsConnected = true;

                    _logger.Information("[StdioTransport.Connect] 连接成功，PID={Pid}", _process.Id);
                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CONNECT: Connection established");
                    _debugFileWriter?.Flush();
                    return true;
                }
                else
                {
                    _logger.Warning("[StdioTransport.Connect] 进程已退出，退出码={ExitCode}", _process.ExitCode);
                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CONNECT: Process exited immediately with code {_process.ExitCode}");
                    _debugFileWriter?.Flush();
                    OnErrorOccurred(new TransportErrorEventArgs($"进程启动后立即退出，退出码={_process.ExitCode}"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.Connect] 启动失败");
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CONNECT-ERROR: {ex.Message}");
                _debugFileWriter?.Flush();
                OnErrorOccurred(new TransportErrorEventArgs($"无法启动进程：{ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    return true;
                }
                try
                {
                    // 记录断开
                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DISCONNECT");
                    _debugFileWriter?.Flush();

                    // 取消读取操作
                    _readCts?.Cancel();
                    // 关闭标准输入
                    _stdin?.Flush();
                    _stdin?.Close();
                    _stdin?.Dispose();
                    // 等待进程退出
                    if (_process != null && !_process.HasExited)
                    {
                        try
                        {
                            _process.Kill();
                            _process.WaitForExit();
                        }
                        catch
                        {
                            // 如果进程无法终止，继续执行
                        }
                    }
                    _stdout?.Dispose();
                    _stderr?.Dispose();
                    _process?.Dispose();

                    // 关闭调试文件
                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CLOSED");
                    _debugFileWriter?.Close();
                    _debugFileWriter?.Dispose();
                    _debugFileWriter = null;

                    IsConnected = false;
                    return true;
                }
                catch (Exception ex)
                {
                    _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DISCONNECT-ERROR: {ex.Message}");
                    _debugFileWriter?.Flush();
                    OnErrorOccurred(new TransportErrorEventArgs($"断开连接时出错：{ex.Message}", ex));
                    return false;
                }
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
                _logger.Warning("[StdioTransport.SendMessage] 失败：未连接或 _stdin 为 null");
                OnErrorOccurred(new TransportErrorEventArgs("传输未连接"));
                return false;
            }

            // 检查进程是否已退出
            if (_process != null && _process.HasExited)
            {
                _logger.Error("[StdioTransport.SendMessage] 失败：进程已退出，退出码={ExitCode}", _process.ExitCode);
                OnErrorOccurred(new TransportErrorEventArgs($"Agent 进程已退出，退出码={_process.ExitCode}"));
                IsConnected = false;
                return false;
            }

            try
            {
                _logger.Information("[StdioTransport.SendMessage] 发送消息：{Message}", message);

                // 写入调试文件 - 绝对记录
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TX: {message}");
                _debugFileWriter?.Flush();

                // 发送消息后添加换行符
                await _stdin.WriteAsync(message + Environment.NewLine).ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] 已写入 stdin，正在 Flush...");

                // 再次记录到调试文件
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TX-WRITTEN");
                _debugFileWriter?.Flush();

                await _stdin.FlushAsync().ConfigureAwait(false);
                _logger.Debug("[StdioTransport.SendMessage] Flush 完成");

                // 记录成功
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TX-FLUSHED");
                _debugFileWriter?.Flush();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.SendMessage] 发送失败");
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TX-ERROR: {ex.Message}");
                _debugFileWriter?.Flush();
                OnErrorOccurred(new TransportErrorEventArgs($"发送消息失败：{ex.Message}", ex));
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
                _logger.Information("[StdioTransport.ReadLoop] 启动读取循环，PID={Pid}", _process?.Id);
                int lineCount = 0;
                // 移除 _stdout.EndOfStream 检查，因为它是一个同步阻塞属性，会导致 ConnectAsync 被阻塞
                while (!cancellationToken.IsCancellationRequested && _stdout != null)
                {
                    var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        _logger.Warning("[StdioTransport.ReadLoop] ReadLine 返回 null，流可能已结束");
                        break;
                    }
                    lineCount++;
                    _logger.Debug("[StdioTransport.ReadLoop] 第{Count}行：{Line}", lineCount, line);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Information("[StdioTransport.ReadLoop] 触发 OnMessageReceived: {Line}", line);
                        // 记录接收到的数据到调试文件
                        _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] RX: {line}");
                        _debugFileWriter?.Flush();
                        OnMessageReceived(new MessageReceivedEventArgs(line));
                    }
                    else
                    {
                        _logger.Debug("[StdioTransport.ReadLoop] 忽略空行");
                    }
                }
                _logger.Warning("[StdioTransport.ReadLoop] 读取循环结束 - 共读取{Count}行，取消={Cancelled}",
                    lineCount, cancellationToken.IsCancellationRequested);
                _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] READ-LOOP-END: 共{lineCount}行");
                _debugFileWriter?.Flush();
            }
            catch (OperationCanceledException)
            {
                _logger.Verbose("[StdioTransport.ReadLoop] 读取循环被取消");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.ReadLoop] 读取循环出错");
                OnErrorOccurred(new TransportErrorEventArgs($"读取输出失败：{ex.Message}", ex));
            }
        }

        /// <summary>
        /// 读取错误循环。
        /// </summary>
        private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information("[StdioTransport.ReadError] 启动错误读取循环，PID={Pid}", _process?.Id);

                while (!cancellationToken.IsCancellationRequested && _stderr != null)
                {
                    var line = await _stderr.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    _logger.Verbose("[StdioTransport.ReadError] 收到 stderr 原始行：'{Line}'", line);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _debugFileWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] STDERR: {line}");
                        _debugFileWriter?.Flush();
                        _logger.Warning("[StdioTransport.ReadError] 进程错误：{Line}", line);
                        OnErrorOccurred(new TransportErrorEventArgs($"进程错误：{line}"));
                    }
                }
                _logger.Warning("[StdioTransport.ReadError] 错误读取循环结束");
            }
            catch (OperationCanceledException)
            {
                _logger.Verbose("[StdioTransport.ReadError] 错误读取循环被取消");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.ReadError] 错误读取循环出错");
                OnErrorOccurred(new TransportErrorEventArgs($"读取错误流失败：{ex.Message}", ex));
            }
        }

        /// <summary>
        /// 进程退出事件处理。
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            IsConnected = false;
            _readCts?.Cancel();
            OnErrorOccurred(new TransportErrorEventArgs("Agent 进程已退出"));
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
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ = DisconnectAsync();
            GC.SuppressFinalize(this);
        }
    }
}
