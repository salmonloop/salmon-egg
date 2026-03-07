using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using UnoAcpClient.Domain.Interfaces.Transport;
using UnoAcpClient.Domain.Models.JsonRpc;
using UnoAcpClient.Domain.Utilities;

namespace UnoAcpClient.Infrastructure.Transport
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

                    _logger.Information("[StdioTransport.Connect] 启动读取循环");
                    // 启动读取循环
                    _ = ReadLoopAsync(_readCts.Token);
                    _ = ReadErrorLoopAsync(_readCts.Token);

                    IsConnected = true;
                    _logger.Information("[StdioTransport.Connect] 连接成功，PID={Pid}", _process.Id);
                    return true;
                }
                else
                {
                    _logger.Warning("[StdioTransport.Connect] 进程已退出，退出码={ExitCode}", _process.ExitCode);
                    OnErrorOccurred(new TransportErrorEventArgs($"进程启动后立即退出，退出码={_process.ExitCode}"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.Connect] 启动失败");
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

                    IsConnected = false;
                    return true;
                }
                catch (Exception ex)
                {
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

            try
            {
                _logger.Verbose("[StdioTransport.SendMessage] 发送消息：{Message}", message);

                // 发送消息后添加换行符
                await _stdin.WriteAsync(message + Environment.NewLine).ConfigureAwait(false);
                _logger.Verbose("[StdioTransport.SendMessage] 已写入，正在 Flush...");
                await _stdin.FlushAsync().ConfigureAwait(false);
                _logger.Verbose("[StdioTransport.SendMessage] Flush 完成");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[StdioTransport.SendMessage] 发送失败");
                OnErrorOccurred(new TransportErrorEventArgs($"发送消息失败：{ex.Message}", ex));
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

                while (!cancellationToken.IsCancellationRequested && _stdout != null && !_stdout.EndOfStream)
                {
                    var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                    _logger.Verbose("[StdioTransport.ReadLoop] 收到原始行：'{Line}'", line);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Verbose("[StdioTransport.ReadLoop] 触发 OnMessageReceived: {Line}", line);
                        OnMessageReceived(new MessageReceivedEventArgs(line));
                    }
                    else
                    {
                        _logger.Verbose("[StdioTransport.ReadLoop] 忽略空行");
                    }
                }

                _logger.Warning("[StdioTransport.ReadLoop] 读取循环结束 - EOF 或取消");
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
                    _logger.Verbose("[StdioTransport.ReadError] 收到 stderr 原始行：'{Line}'", line);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
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
            DisconnectAsync().Wait();
            GC.SuppressFinalize(this);
        }
    }
}
